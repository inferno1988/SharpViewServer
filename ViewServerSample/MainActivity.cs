﻿using Android.App;
using Android.Widget;
using Android.OS;
using SharpViewServer;

namespace ViewServerSample
{
    [Activity(Label = "ViewServerSample", MainLauncher = true, Icon = "@mipmap/icon")]
    public class MainActivity : Activity
    {
        int count = 1;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            // Get our button from the layout resource,
            // and attach an event to it
            Button button = FindViewById<Button>(Resource.Id.myButton);
            
            button.Click += delegate
            {
                button.Text = string.Format("{0} clicks!", count++);
            };
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
}


