# SharpViewServer
ViewServer is a simple class you can use in your Android application
to use the HierarchyViewer inspection tool.

ViewServer requires the Android SDK r12 or higher.
http://developer.android.com/sdk/index.html

ViewServer.cs class can be used to enable the use of HierarchyViewer inside an application. HierarchyViewer is an Android SDK tool that can be used to inspect and debug the user interface of running applications. For security reasons, HierarchyViewer does not work on production builds (for instance phones bought in store.) By using this class, you can make HierarchyViewer work on any device. You must be very careful however to only enable HierarchyViewer when debugging your application.

To use this view server, your application **must require the INTERNET permission.**

The recommended way to use this API is to register activities when they are created, and to unregister them when they get destroyed:
```cs
    public class MainActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.Main);
            ViewServer.Get(this).AddWindow(this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ViewServer.Get(this).RemoveWindow(this);
        }

        protected override void OnResume()
        {
            base.OnResume();
            ViewServer.Get(this).SetFocusedWindow(this);
        }
    }
```

Port of Romain Guys ViewServer (https://github.com/romainguy/ViewServer)
