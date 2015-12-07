using Android.Runtime;

namespace SharpViewServer
{
    public interface IWindowListener : IJavaObject
    {
        void WindowsChanged();

        void FocusChanged();
    }
}

