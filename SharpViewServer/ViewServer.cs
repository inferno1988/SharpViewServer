/*
 * Copyright (C) 2011 The Android Open Source Project
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

/**
* <p>This class can be used to enable the use of HierarchyViewer inside an
* application. HierarchyViewer is an Android SDK tool that can be used
* to inspect and debug the user interface of running applications. For
* security reasons, HierarchyViewer does not work on production builds
* (for instance phones bought in store.) By using this class, you can
* make HierarchyViewer work on any device. You must be very careful
* however to only enable HierarchyViewer when debugging your
* application.</p>
* 
* <p>To use this view server, your application must require the INTERNET
* permission.</p>
* 
* <p>The recommended way to use this API is to register activities when
* they are created, and to unregister them when they get destroyed:</p>
* 
* <pre>
* public class MyActivity extends Activity {
    *     public void onCreate(Bundle savedInstanceState) {
        *         super.onCreate(savedInstanceState);
        *         // Set content view, etc.
        *         ViewServer.get(this).addWindow(this);
        *     }
    *       
    *     public void onDestroy() {
        *         super.onDestroy();
        *         ViewServer.get(this).removeWindow(this);
        *     }
    *   
    *     public void onResume() {
        *         super.onResume();
        *         ViewServer.get(this).setFocusedWindow(this);
        *     }
    * }
* </pre>
* 
* <p>
* In a similar fashion, you can use this API with an InputMethodService:
* </p>
* 
* <pre>
* public class MyInputMethodService extends InputMethodService {
    *     public void onCreate() {
        *         super.onCreate();
        *         View decorView = getWindow().getWindow().getDecorView();
        *         String name = "MyInputMethodService";
        *         ViewServer.get(this).addWindow(decorView, name);
        *     }
    *
    *     public void onDestroy() {
        *         super.onDestroy();
        *         View decorView = getWindow().getWindow().getDecorView();
        *         ViewServer.get(this).removeWindow(decorView);
        *     }
    *
    *     public void onStartInput(EditorInfo attribute, boolean restarting) {
        *         super.onStartInput(attribute, restarting);
        *         View decorView = getWindow().getWindow().getDecorView();
        *         ViewServer.get(this).setFocusedWindow(decorView);
        *     }
    * }
* </pre>
*/

using Java.Lang;
using Java.Net;
using Android.Content;
using Android.Content.PM;
using System.Collections.Generic;
using Android.Views;
using Android.OS;
using Android.Util;
using Java.Util.Concurrent;
using Java.Util.Concurrent.Locks;
using Android.App;
using Android.Text;

namespace SharpViewServer
{
    public class ViewServer : Java.Lang.Object, IRunnable
    {
        /**
     * The default port used to start view servers.
     */
        public const int VIEW_SERVER_DEFAULT_PORT = 4939;
        public const int VIEW_SERVER_MAX_CONNECTIONS = 10;
        public const string BUILD_TYPE_USER = "user";

        // Debug facility
        internal const string LOG_TAG = "ViewServer";

        internal const string VALUE_PROTOCOL_VERSION = "4";
        internal const string VALUE_SERVER_VERSION = "4";

        // Protocol commands
        // Returns the protocol version
        private const string COMMAND_PROTOCOL_VERSION = "PROTOCOL";
        // Returns the server version
        private const string COMMAND_SERVER_VERSION = "SERVER";
        // Lists all of the available windows in the system
        private const string COMMAND_WINDOW_MANAGER_LIST = "LIST";
        // Keeps a connection open and notifies when the list of windows changes
        private const string COMMAND_WINDOW_MANAGER_AUTOLIST = "AUTOLIST";
        // Returns the focused window
        private const string COMMAND_WINDOW_MANAGER_GET_FOCUS = "GET_FOCUS";

        private ServerSocket mServer;
        private readonly int mPort;

        private Thread mThread;
        private IExecutorService mThreadPool;

        private readonly CopyOnWriteArrayList mListeners =
            new global::Java.Util.Concurrent.CopyOnWriteArrayList();

        internal  readonly Dictionary<View, string> mWindows = new Dictionary<View, string>();
        internal  readonly ReentrantReadWriteLock mWindowsLock = new ReentrantReadWriteLock();

        internal  View mFocusedWindow;
        internal  readonly ReentrantReadWriteLock mFocusLock = new ReentrantReadWriteLock();

        private static ViewServer sServer;

        /**
     * Returns a unique instance of the ViewServer. This method should only be
     * called from the main thread of your application. The server will have
     * the same lifetime as your process.
     * 
     * If your application does not have the <code>android:debuggable</code>
     * flag set in its manifest, the server returned by this method will
     * be a dummy object that does not do anything. This allows you to use
     * the same code in debug and release versions of your application.
     * 
     * @param context A Context used to check whether the application is
     *                debuggable, this can be the application context
     */
        public static ViewServer Get(Context context)
        {
            ApplicationInfo info = context.ApplicationInfo;
            if (ViewServer.BUILD_TYPE_USER.Equals(Build.Type) &&
                (info.Flags & ApplicationInfoFlags.Debuggable) != 0)
            {
                if (sServer == null)
                {
                    sServer = new ViewServer(ViewServer.VIEW_SERVER_DEFAULT_PORT);
                }

                if (!sServer.IsRunning())
                {
                    try
                    {
                        sServer.Start();
                    }
                    catch (Java.IO.IOException e)
                    {
                        Log.Debug(LOG_TAG, "Error:", e);
                    }
                }
            }
            else
            {
                sServer = new NoopViewServer();
            }

            return sServer;
        }

        internal ViewServer()
        {
            mPort = -1;
        }

        /**
     * Creates a new ViewServer associated with the specified window manager on the
     * specified local port. The server is not started by default.
     *
     * @param port The port for the server to listen to.
     *
     * @see #start()
     */
        internal ViewServer(int port)
        {
            mPort = port;
        }

        /**
     * Starts the server.
     *
     * @return True if the server was successfully created, or false if it already exists.
     * @throws IOException If the server cannot be created.
     *
     * @see #stop()
     * @see #isRunning()
     * @see WindowManagerService#startViewServer(int)
     */
        public virtual bool Start()
        {
            if (mThread != null)
            {
                return false;
            }

            mThread = new Thread(this, "Local View Server [port=" + mPort + "]");
            mThreadPool = Executors.NewFixedThreadPool(VIEW_SERVER_MAX_CONNECTIONS);
            mThread.Start();

            return true;
        }

        /**
     * Stops the server.
     *
     * @return True if the server was stopped, false if an error occurred or if the
     *         server wasn't started.
     *
     * @see #start()
     * @see #isRunning()
     * @see WindowManagerService#stopViewServer()
     */
        public virtual bool Stop()
        {
            if (mThread != null)
            {
                mThread.Interrupt();
                if (mThreadPool != null)
                {
                    try
                    {
                        mThreadPool.ShutdownNow();
                    }
                    catch (SecurityException e)
                    {
                        Log.Warn(LOG_TAG, "Could not stop all view server threads");
                    }
                }

                mThreadPool = null;
                mThread = null;

                try
                {
                    mServer.Close();
                    mServer = null;
                    return true;
                }
                catch (Java.IO.IOException e)
                {
                    Log.Warn(LOG_TAG, "Could not close the view server");
                }
            }

            mWindowsLock.WriteLock().Lock();
            try
            {
                mWindows.Clear();
            }
            finally
            {
                mWindowsLock.WriteLock().Unlock();
            }

            mFocusLock.WriteLock().Lock();
            try
            {
                mFocusedWindow = null;
            }
            finally
            {
                mFocusLock.WriteLock().Unlock();
            }

            return false;
        }

        /**
     * Indicates whether the server is currently running.
     *
     * @return True if the server is running, false otherwise.
     *
     * @see #start()
     * @see #stop()
     * @see WindowManagerService#isViewServerRunning()  
     */
        public virtual bool IsRunning()
        {
            return mThread != null && mThread.IsAlive;
        }

        /**
     * Invoke this method to register a new view hierarchy.
     * 
     * @param activity The activity whose view hierarchy/window to register
     * 
     * @see #addWindow(View, String)
     * @see #removeWindow(Activity)
     */
        public virtual void AddWindow(Activity activity)
        {
            string name = activity.Title.ToString();
            if (TextUtils.IsEmpty(name))
            {
                name = activity.Class.CanonicalName +
                "/0x" + JavaSystem.IdentityHashCode(activity);
            }
            else
            {
                name += "(" + activity.Class.CanonicalName + ")";
            }
            AddWindow(activity.Window.DecorView, name);
        }

        /**
     * Invoke this method to unregister a view hierarchy.
     * 
     * @param activity The activity whose view hierarchy/window to unregister
     * 
     * @see #addWindow(Activity)
     * @see #removeWindow(View)
     */
        public virtual void RemoveWindow(Activity activity)
        {
            RemoveWindow(activity.Window.DecorView);
        }

        /**
     * Invoke this method to register a new view hierarchy.
     * 
     * @param view A view that belongs to the view hierarchy/window to register
     * @name name The name of the view hierarchy/window to register
     * 
     * @see #removeWindow(View)
     */
        public virtual void AddWindow(View view, string name)
        {
            mWindowsLock.WriteLock().Lock();
            try
            {
                mWindows.Add(view.RootView, name);
            }
            finally
            {
                mWindowsLock.WriteLock().Unlock();
            }
            fireWindowsChangedEvent();
        }

        /**
     * Invoke this method to unregister a view hierarchy.
     * 
     * @param view A view that belongs to the view hierarchy/window to unregister
     * 
     * @see #addWindow(View, String)
     */
        public virtual void RemoveWindow(View view)
        {
            View rootView;
            mWindowsLock.WriteLock().Lock();
            try
            {
                rootView = view.RootView;
                mWindows.Remove(rootView);
            }
            finally
            {
                mWindowsLock.WriteLock().Unlock();
            }
            mFocusLock.WriteLock().Lock();
            try
            {
                if (mFocusedWindow == rootView)
                {
                    mFocusedWindow = null;
                }
            }
            finally
            {
                mFocusLock.WriteLock().Unlock();
            }
            fireWindowsChangedEvent();
        }

        /**
     * Invoke this method to change the currently focused window.
     * 
     * @param activity The activity whose view hierarchy/window hasfocus,
     *                 or null to remove focus
     */
        public virtual void SetFocusedWindow(Activity activity)
        {
            SetFocusedWindow(activity.Window.DecorView);
        }

        /**
     * Invoke this method to change the currently focused window.
     * 
     * @param view A view that belongs to the view hierarchy/window that has focus,
     *             or null to remove focus
     */
        public virtual void SetFocusedWindow(View view)
        {
            mFocusLock.WriteLock().Lock();
            try
            {
                mFocusedWindow = view == null ? null : view.RootView;
            }
            finally
            {
                mFocusLock.WriteLock().Unlock();
            }
            fireFocusChangedEvent();
        }

        /**
     * Main server loop.
     */
        public virtual void Run()
        {
            try
            {
                mServer = new ServerSocket(mPort, VIEW_SERVER_MAX_CONNECTIONS, InetAddress.LocalHost);
            }
            catch (Java.Lang.Exception e)
            {
                Log.Warn(LOG_TAG, "Starting ServerSocket error: ", e);
            }

            while (mServer != null && Thread.CurrentThread() == mThread)
            {
                // Any uncaught exception will crash the system process
                try
                {
                    Socket client = mServer.Accept();
                    if (mThreadPool != null)
                    {
                        mThreadPool.Submit(new ViewServerWorker(client, this));
                    }
                    else
                    {
                        try
                        {
                            client.Close();
                        }
                        catch (Java.IO.IOException e)
                        {
                            e.PrintStackTrace();
                        }
                    }
                }
                catch (Java.Lang.Exception e)
                {
                    Log.Warn(LOG_TAG, "Connection error: ", e);
                }
            }
        }

        internal static bool WriteValue(Socket client, string value)
        {
            bool result;
            Java.IO.BufferedWriter output = null;
            try
            {
                var clientStream = client.OutputStream;
                output = new Java.IO.BufferedWriter(new Java.IO.OutputStreamWriter(clientStream), 8 * 1024);
                output.Write(value);
                output.Write("\n");
                output.Flush();
                result = true;
            }
            catch (Java.Lang.Exception e)
            {
                result = false;
            }
            finally
            {
                if (output != null)
                {
                    try
                    {
                        output.Close();
                    }
                    catch (Java.IO.IOException e)
                    {
                        result = false;
                    }
                }
            }
            return result;
        }

        private void fireWindowsChangedEvent()
        {
            for (int i = 0; i < mListeners.Size(); i++)
            {
                ((IWindowListener)mListeners.Get(i)).WindowsChanged();
            }
        }

        private void fireFocusChangedEvent()
        {
            for (int i = 0; i < mListeners.Size(); i++)
            {
                ((IWindowListener)mListeners.Get(i)).FocusChanged();
            }
        }

        internal void AddWindowListener(IWindowListener listener)
        {
            var windowListener = (Java.Lang.Object)listener;
            if (!mListeners.Contains(windowListener))
            {
                mListeners.Add(windowListener);
            }
        }

        internal void RemoveWindowListener(IWindowListener listener)
        {
            var windowListener = (Java.Lang.Object)listener;
            mListeners.Remove(windowListener);
        }
    }
}
