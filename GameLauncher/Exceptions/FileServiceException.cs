namespace GameLauncher.Exceptions
{
    public class FileServiceException : Exception
    {
        public string FilePath { get; private set; }
        public FileServiceOperation Operation { get; set; }

        public FileServiceException(string message, FileServiceOperation operation, string filePath, Exception inner = null) : base(message)
        {
            FilePath = filePath;
            Operation = operation;
        }
    }

    public enum FileServiceOperation
    {
        Clean, Backup, Delete, Extract, Verify
    }
}
