﻿//#define SINGLE_THREADED

#region License
//
// (C) Copyright 2010 Patrick Cozzi and Deron Ohlarik
//
// Distributed under the Boost Software License, Version 1.0.
// See License.txt or http://www.boost.org/LICENSE_1_0.txt.
//
#endregion

using System;
using System.Threading;
using System.Drawing;
using System.Collections.Generic;

using OpenGlobe.Core;
using OpenGlobe.Core.Geometry;
using OpenGlobe.Core.Tessellation;
using OpenGlobe.Renderer;
using OpenGlobe.Scene;

namespace OpenGlobe.Examples.Chapter9
{
    internal class ShapefileRequest
    {
        public ShapefileRequest(string filename, string bitmapFilename, ShapefileType type)
        {
            _filename = filename;
            _bitmapFilename = bitmapFilename;
            _type = type;
        }

        public string Filename { get { return _filename; } }
        public string BitmapFilename { get { return _bitmapFilename; } }
        public ShapefileType Type { get { return _type; } }

        private string _filename;
        private string _bitmapFilename;
        private ShapefileType _type;
    }

    internal class ShapefileWorker
    {
        public ShapefileWorker(GraphicsWindow window, Ellipsoid globeShape, MessageQueue doneQueue)
        {
            _window = window;
            _globeShape = globeShape;
            _doneQueue = doneQueue;
        }

        public void Process(object sender, MessageQueueEventArgs e)
        {
#if !SINGLE_THREADED
            _window.MakeCurrent();
#endif

            ShapefileRequest request = (ShapefileRequest)e.Message;
            IRenderable shapefile = null;

            if (request.Type == ShapefileType.Polyline)
            {
                PolylineShapefile polylineShapefile = new PolylineShapefile(request.Filename, _window.Context, _globeShape);
                polylineShapefile.DepthWrite = false;
                shapefile = polylineShapefile;
            }
            else if (request.Type == ShapefileType.Polygon)
            {
                PolygonShapefile polygonShapefile = new PolygonShapefile(request.Filename, _window.Context, _globeShape);
                polygonShapefile.DepthWrite = false;
                shapefile = polygonShapefile;
            }
            else if (request.Type == ShapefileType.Point)
            {
                PointShapefile pointShapefile = new PointShapefile(request.Filename, null, _window.Context, _globeShape, new Bitmap(request.BitmapFilename));
                pointShapefile.DepthWrite = false;
                shapefile = pointShapefile;
            }
            else
            {
                throw new ArgumentException("request.Type");
            }

#if !SINGLE_THREADED
            Fence fence = Device.CreateFence();
            while (fence.ClientWait(0) == ClientWaitResult.TimeoutExpired)
            {
                Thread.Sleep(10);   // TODO:  Other work
            }
#endif

            _doneQueue.Post(shapefile);
        }

        private readonly GraphicsWindow _window;
        private readonly Ellipsoid _globeShape;
        private readonly MessageQueue _doneQueue;
    }
    
    sealed class Multithreading : IDisposable
    {
        public Multithreading()
        {
            Ellipsoid globeShape = Ellipsoid.ScaledWgs84;

            _workerWindow = Device.CreateWindow(1, 1);                                  // Needs to be created first for whatever reason
            _window = Device.CreateWindow(800, 600, "Chapter 9:  Multithreading");
            _window.Resize += OnResize;
            _window.RenderFrame += OnRenderFrame;
            _sceneState = new SceneState();
            _camera = new CameraLookAtPoint(_sceneState.Camera, _window, globeShape);
            _clearState = new ClearState();

            Bitmap bitmap = new Bitmap("NE2_50M_SR_W_4096.jpg");
            _texture = Device.CreateTexture2D(bitmap, TextureFormat.RedGreenBlue8, false);

            _globe = new RayCastedGlobe(_window.Context);
            _globe.Shape = globeShape;
            _globe.Texture = _texture;
            _globe.UseAverageDepth = true;

            ///////////////////////////////////////////////////////////////////

            _doneQueue.MessageReceived += ProcessNewShapefile;

            _requestQueue.MessageReceived += new ShapefileWorker(_workerWindow, globeShape, _doneQueue).Process;

            // TODO:  Draw order
            _requestQueue.Post(new ShapefileRequest("110m_admin_0_countries.shp", "", ShapefileType.Polygon));
            _requestQueue.Post(new ShapefileRequest("110m_admin_1_states_provinces_lines_shp.shp", "", ShapefileType.Polyline));
            _requestQueue.Post(new ShapefileRequest("airprtx020.shp", "paper-plane--arrow.png", ShapefileType.Point));
            _requestQueue.Post(new ShapefileRequest("amtrakx020.shp", "car-red.png", ShapefileType.Point));
            _requestQueue.Post(new ShapefileRequest("110m_populated_places_simple.shp", "032.png", ShapefileType.Point));

#if SINGLE_THREADED
            _requestQueue.ProcessQueue();
#else
            _requestQueue.StartInAnotherThread();
#endif

            ///////////////////////////////////////////////////////////////////

            _sceneState.Camera.ZoomToTarget(globeShape.MaximumRadius);
        }

        private void OnResize()
        {
            _window.Context.Viewport = new Rectangle(0, 0, _window.Width, _window.Height);
            _sceneState.Camera.AspectRatio = _window.Width / (double)_window.Height;
        }

        private void OnRenderFrame()
        {
            _doneQueue.ProcessQueue();

            Context context = _window.Context;
            context.Clear(_clearState);
            _globe.Render(context, _sceneState);

            foreach (IRenderable shapefile in _shapefiles)
            {
                shapefile.Render(context, _sceneState);
            }
        }

        public void ProcessNewShapefile(object sender, MessageQueueEventArgs e)
        {
            _shapefiles.Add((IRenderable)e.Message);
        }

        #region IDisposable Members

        public void Dispose()
        {
            foreach (IRenderable shapefile in _shapefiles)
            {
                (shapefile as IDisposable).Dispose();
            }

            _doneQueue.Dispose();
            _requestQueue.Dispose();
            _texture.Dispose();
            _globe.Dispose();
            _camera.Dispose();
            _window.Dispose();
            _workerWindow.Dispose();
        }

        #endregion

        private void Run(double updateRate)
        {
            _window.Run(updateRate);
        }

        static void Main()
        {
            using (Multithreading example = new Multithreading())
            {
                example.Run(30.0);
            }
        }

        private readonly GraphicsWindow _window;
        private readonly SceneState _sceneState;
        private readonly CameraLookAtPoint _camera;
        private readonly ClearState _clearState;
        private readonly RayCastedGlobe _globe;
        private readonly Texture2D _texture;

        private readonly IList<IRenderable> _shapefiles = new List<IRenderable>();

        private readonly MessageQueue _requestQueue = new MessageQueue();
        private readonly MessageQueue _doneQueue = new MessageQueue();
        private readonly GraphicsWindow _workerWindow;
    }
}