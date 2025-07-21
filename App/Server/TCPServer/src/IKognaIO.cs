using System;

namespace TCPServer
{
    public interface IKognaIO : IDisposable
    {
        // Properties
        bool Connected { get; set; }
        string ErrMsg { get; set; }
        bool FailMessageAlreadyShown { get; set; }
        bool SendAbortOnConnect { get; set; }
        int NonRespondingCount { get; set; }
        bool BoardIDAssigned { get; set; }
        
        // I/O Methods
        int WriteLine(int channel, string message);
        int WriteLineReadLine(int channel, string message, out string response);
        int ReadLineTimeOut(int channel, out string response, int timeoutMs);
        
        // Connection Management
        int Connect();
        
        // Console Handling
        void SetConsoleCallback(ServerConsoleHandler handler);
        void ServiceConsole();
    }
}
