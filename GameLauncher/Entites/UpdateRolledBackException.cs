using System;

namespace GameLauncher.Entites
{
    public class UpdateRolledBackException : Exception
    {
        public UpdateRolledBackException() { }

        public UpdateRolledBackException(string message) : base(message) { }

        public UpdateRolledBackException(string message, Exception innerException) : base(message, innerException) { }
    }
}
