using Java.Lang;
using Java.Net;
using Java.IO;
using Android.Util;
using Java.Lang.Reflect;
using Android.Views;
using System.Threading;

namespace SharpViewServer
{
    public  class ViewServerWorker : Java.Lang.Object, IRunnable, IWindowListener
    {
        private const string LOG_TAG = "ViewServer";
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

        private Socket mClient;
        private bool mNeedWindowListUpdate;
        private bool mNeedFocusedWindowUpdate;

        private ViewServer mSharpViewServer;
        private readonly ManualResetEvent mre = new ManualResetEvent(false);

        public ViewServerWorker(Socket client, ViewServer sharpViewServer)
        {
            mSharpViewServer = sharpViewServer;
            mClient = client;
            mNeedWindowListUpdate = false;
            mNeedFocusedWindowUpdate = false;
        }

        public void Run()
        {
            BufferedReader input = null;
            try
            {
                input = new BufferedReader(new InputStreamReader(mClient.InputStream), 1024);

                string request = input.ReadLine();

                string command;
                string parameters;

                int index = request.IndexOf(' ');
                if (index == -1)
                {
                    command = request;
                    parameters = "";
                }
                else
                {
                    command = request.Substring(0, index);
                    parameters = request.Substring(index + 1);
                }

                bool result;
                if (COMMAND_PROTOCOL_VERSION.Equals(command))
                {
                    result = ViewServer.WriteValue(mClient, ViewServer.VALUE_PROTOCOL_VERSION);
                }
                else if (COMMAND_SERVER_VERSION.Equals(command))
                {
                    result = ViewServer.WriteValue(mClient, ViewServer.VALUE_SERVER_VERSION);
                }
                else if (COMMAND_WINDOW_MANAGER_LIST.Equals(command))
                {
                    result = ListWindows(mClient);
                }
                else if (COMMAND_WINDOW_MANAGER_GET_FOCUS.Equals(command))
                {
                    result = GetFocusedWindow(mClient);
                }
                else if (COMMAND_WINDOW_MANAGER_AUTOLIST.Equals(command))
                {
                    result = WindowManagerAutolistLoop();
                }
                else
                {
                    result = WindowCommand(mClient, command, parameters);
                }

                if (!result)
                {
                    Log.Warn(LOG_TAG, "An error occurred with the command: " + command);
                }
            }
            catch (IOException e)
            {
                Log.Warn(LOG_TAG, "Connection error: ", e);
            }
            finally
            {
                if (input != null)
                {
                    try
                    {
                        input.Close();

                    }
                    catch (IOException e)
                    {
                        e.PrintStackTrace();
                    }
                }
                if (mClient != null)
                {
                    try
                    {
                        mClient.Close();
                    }
                    catch (IOException e)
                    {
                        e.PrintStackTrace();
                    }
                }
            }
        }

        private bool WindowCommand(Socket client, string command, string parameters)
        {
            bool success = true;
            BufferedWriter output = null;

            try
            {
                // Find the hash code of the window
                int index = parameters.IndexOf(' ');
                if (index == -1)
                {
                    index = parameters.Length;
                }
                string code = parameters.Substring(0, index);
                int hashCode = (int)Long.ParseLong(code, 16);

                // Extract the command's parameter after the window description
                if (index < parameters.Length)
                {
                    parameters = parameters.Substring(index + 1);
                }
                else
                {
                    parameters = "";
                }

                View window = FindWindow(hashCode);
                if (window == null)
                {
                    return false;
                }

                // call stuff
                Method dispatch = Class.FromType(typeof(ViewDebug)).GetDeclaredMethod("dispatchCommand",
                                      Class.FromType(typeof(View)), Class.FromType(typeof(Java.Lang.String)),
                                      Class.FromType(typeof(Java.Lang.String)), Class.FromType(typeof(OutputStream)));
                
                dispatch.Accessible = true;
                dispatch.Invoke(null, window, command, parameters,
                    new UncloseableOutputStream(client.OutputStream));

                if (!client.IsOutputShutdown)
                {
                    output = new BufferedWriter(new OutputStreamWriter(client.OutputStream));
                    output.Write("DONE\n");
                    output.Flush();
                }

            }
            catch (Java.Lang.Exception e)
            {
                Log.Warn(LOG_TAG, "Could not send command " + command +
                    " with parameters " + parameters, e);
                success = false;
            }
            finally
            {
                if (output != null)
                {
                    try
                    {
                        output.Close();
                    }
                    catch (IOException e)
                    {
                        success = false;
                    }
                }
            }

            return success;
        }

        private View FindWindow(int hashCode)
        {
            if (hashCode == -1)
            {
                View window = null;
                mSharpViewServer.mWindowsLock.ReadLock().Lock();
                try
                {
                    window = mSharpViewServer.mFocusedWindow;
                }
                finally
                {
                    mSharpViewServer.mWindowsLock.ReadLock().Unlock();
                }
                return window;
            }


            mSharpViewServer.mWindowsLock.ReadLock().Lock();
            try
            {
                foreach (var entry in mSharpViewServer.mWindows)
                {
                    if (JavaSystem.IdentityHashCode(entry.Key) == hashCode)
                    {
                        return entry.Key;
                    }
                }
            }
            finally
            {
                mSharpViewServer.mWindowsLock.ReadLock().Unlock();
            }

            return null;
        }

        private bool ListWindows(Socket client)
        {
            bool result = true;
            BufferedWriter output = null;

            try
            {
                mSharpViewServer.mWindowsLock.ReadLock().Lock();

                var clientStream = client.OutputStream;
                output = new BufferedWriter(new OutputStreamWriter(clientStream), 8 * 1024);

                foreach (var entry in mSharpViewServer.mWindows)
                {
                    output.Write(Integer.ToHexString(JavaSystem.IdentityHashCode(entry.Key)));
                    output.Write(' ');
                    output.Append(entry.Value);
                    output.Write('\n');
                }

                output.Write("DONE.\n");
                output.Flush();
            }
            catch (Java.Lang.Exception e)
            {
                result = false;
            }
            finally
            {
                mSharpViewServer.mWindowsLock.ReadLock().Unlock();

                if (output != null)
                {
                    try
                    {
                        output.Close();
                    }
                    catch (IOException e)
                    {
                        result = false;
                    }
                }
            }

            return result;
        }

        private bool GetFocusedWindow(Socket client)
        {
            bool result = true;
            string focusName = null;

            BufferedWriter output = null;
            try
            {
                var clientStream = client.OutputStream;
                output = new BufferedWriter(new OutputStreamWriter(clientStream), 8 * 1024);

                View focusedWindow = null;

                mSharpViewServer.mFocusLock.ReadLock().Lock();
                try
                {
                    focusedWindow = mSharpViewServer.mFocusedWindow;
                }
                finally
                {
                    mSharpViewServer.mFocusLock.ReadLock().Unlock();
                }

                if (focusedWindow != null)
                {
                    mSharpViewServer.mWindowsLock.ReadLock().Lock();
                    try
                    {
                        focusName = mSharpViewServer.mWindows[mSharpViewServer.mFocusedWindow];
                    }
                    finally
                    {
                        mSharpViewServer.mWindowsLock.ReadLock().Unlock();
                    }

                    output.Write(Integer.ToHexString(JavaSystem.IdentityHashCode(focusedWindow)));
                    output.Write(' ');
                    output.Append(focusName);
                }
                output.Write('\n');
                output.Flush();
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
                    catch (IOException e)
                    {
                        result = false;
                    }
                }
            }

            return result;
        }

        public void WindowsChanged()
        {
            mNeedWindowListUpdate = true;
            mre.Set();
        }

        public void FocusChanged()
        {
            mNeedFocusedWindowUpdate = true;
            mre.Set();
        }

        private bool WindowManagerAutolistLoop()
        {
            mSharpViewServer.AddWindowListener(this);
            BufferedWriter output = null;
            try
            {
                output = new BufferedWriter(new OutputStreamWriter(mClient.OutputStream));
                while (!Java.Lang.Thread.Interrupted())
                {
                    bool needWindowListUpdate = false;
                    bool needFocusedWindowUpdate = false;

                    while (!mNeedWindowListUpdate && !mNeedFocusedWindowUpdate)
                    {
                        mre.WaitOne();
                        mre.Reset();
                    }
                    if (mNeedWindowListUpdate)
                    {
                        mNeedWindowListUpdate = false;
                        needWindowListUpdate = true;
                    }
                    if (mNeedFocusedWindowUpdate)
                    {
                        mNeedFocusedWindowUpdate = false;
                        needFocusedWindowUpdate = true;
                    }

                    if (needWindowListUpdate)
                    {
                        output.Write("LIST UPDATE\n");
                        output.Flush();
                    }
                    if (needFocusedWindowUpdate)
                    {
                        output.Write("FOCUS UPDATE\n");
                        output.Flush();
                    }
                }
            }
            catch (Java.Lang.Exception e)
            {
                Log.Warn(LOG_TAG, "Connection error: ", e);
            }
            finally
            {
                if (output != null)
                {
                    try
                    {
                        output.Close();
                    }
                    catch (IOException e)
                    {
                        // Ignore
                    }
                }
                mSharpViewServer.RemoveWindowListener(this);
            }
            return true;
        }
    }

}

