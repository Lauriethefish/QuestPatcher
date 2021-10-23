using System;
using System.IO;

namespace QuestPatcher.Core
{
    public class TempFile : IDisposable
    {
        public string Path
        {
            get
            {
                if (_path == null)
                {
                    throw new ObjectDisposedException(GetType().Name);
                }

                return _path;
            }
        }
    
        private string? _path;
    
        public TempFile(string path)
        {
            _path = path;
        }

        public TempFile()
        {
            _path = System.IO.Path.GetTempFileName();
        }

        ~TempFile() { Dispose(); }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            DisposeInternal();
        }
            
        private void DisposeInternal()
        {
            if (_path == null) { return; }
            if (!File.Exists(_path)) { return; }
            try { File.Delete(_path); } catch { } // Cannot reasonably do anything about this error

            _path = null;
        }
        
        public override string ToString()
        {
            return _path ?? throw new ObjectDisposedException(GetType().Name);
        }
    }
}
