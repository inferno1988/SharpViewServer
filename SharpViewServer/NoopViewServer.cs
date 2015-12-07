using Android.App;
using Android.Views;

namespace SharpViewServer
{
    internal class NoopViewServer : ViewServer
    {
        public NoopViewServer()
            : base()
        {
        }

        public override bool Start()
        {
            return false;
        }

        public override bool Stop()
        {
            return false;
        }

        public override bool IsRunning()
        {
            return false;
        }

        public override void AddWindow(Activity activity)
        {
        }

        public override void RemoveWindow(Activity activity)
        {
        }

        public override void AddWindow(View view, string name)
        {
        }

        public override void RemoveWindow(View view)
        {
        }

        public override void SetFocusedWindow(Activity activity)
        {
        }

        public override void SetFocusedWindow(View view)
        {
        }

        public override void Run()
        {
        }
    }
}

