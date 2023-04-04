namespace SocketStorm;

public class ExceptionThrownEventArgs : EventArgs
{
    public Exception Exception { get; }

    public ExceptionThrownEventArgs(Exception exception) => Exception = exception;
}
