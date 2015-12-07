using Java.IO;

namespace SharpViewServer
{
    internal class UncloseableOutputStream : OutputStream
    {
        private readonly System.IO.Stream mStream;

        internal UncloseableOutputStream(System.IO.Stream stream)
        {
            mStream = stream;
        }

        public override void Close()
        {
        }

        public override bool Equals(Java.Lang.Object o)
        {
            return mStream.Equals(o);
        }


        public override void Flush()
        {
            mStream.Flush();
        }

        public override int GetHashCode()
        {
            return mStream.GetHashCode();
        }

        public override string ToString()
        {
            return mStream.ToString();
        }

        public override void Write(int oneByte)
        {
            mStream.WriteByte((byte)oneByte);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            mStream.Write(buffer, offset, count);
        }

        public override void Write(byte[] buffer)
        {
            mStream.Write(buffer, 0, buffer.Length);
        }
    }
}

