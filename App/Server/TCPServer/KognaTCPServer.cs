

namespace TCPServer;

public class KServer
{
    //public string? ipAddress;
    //public int? port;
    public event Action<KognaStatus>? OnStatusUpdate;
    private CancellationTokenSource _cts = new();

    public  KognaIO? _io { get; set; }
    public  KognaMotion _motion {get; set;}
    public  KognaMonitor _monitor {get; set;}


    public KServer(string ipAddress, int port, KognaMonitor monitor, KognaMotion motion, KognaIO io)
    {
        _io = io;
        _motion = motion;
        _monitor = monitor;
    }


    public bool Start()
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] KognaServerHost.Start() called");
        // ** establish TCP link to the Kogna device **
        int connResult = _io!.Connect();
        if (connResult != KognaIO.KOGNA_OK)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR: Could not connect to Kogna at {_io.USBLocation()}. " + $"Code={connResult}, ErrMsg={_io.ErrMsg}");
            return false;
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Connected to Kogna at {_io.USBLocation()}");
            _monitor.OnStatusUpdate += s => OnStatusUpdate?.Invoke(s);
            _ = _monitor.StartAsyncMonitor(_cts.Token);
            return true;
        }
    }
    public bool Close()
    {

        _cts.Cancel();
        _io!.Dispose();
        //_coord.Dispose();
        //_monitor.Dispose();
        return true;
    }

}

    public class IpcRequest
    {
        public string Command { get; set; } = null!;
        public string[] Args { get; set; } = null!;
    }

    public class IpcResponse
    {
        public string Status { get; set; } = null!;
        public string Result { get; set; } = null!;
        public string Error { get; set; } = null!;
    }