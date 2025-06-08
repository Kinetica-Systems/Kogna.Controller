using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KognaServer.Server;

namespace KognaServer.Server.KognaServer
{
    /// <summary>
    /// Named‐pipe server that dispatches client requests into the in‐process KognaIO.
    /// </summary>
    public class KognaServerMain : IDisposable
    {
        private const string PipeName = "kmotionpipe";
        private const int    BufferSize = 4096;
        private readonly CancellationTokenSource _cts = new();
        private readonly KognaIO _io = null!;
        private readonly ConcurrentBag<Task> _clientTasks = new();
        public event Action<bool>? ConnectionChanged;
        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    ConnectionChanged?.Invoke(value);
                }
            }
        }
        public KognaServerMain(string ipAddress, int port)
        {

            _io = new KognaIO(ipAddress, port);
            
        }

        /// <summary>Start listening for pipe clients.</summary>
        public void Start()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] KMotionServerHost.Start() called");
            // ** establish TCP link to the Kogna device **
                        int connResult = _io.Connect();
                        if (connResult != KognaIO.KOGNA_OK)
                            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR: Could not connect to Kogna at {_io.USBLocation()}. " +
                                    $"Code={connResult}, ErrMsg={_io.ErrMsg}");
                // optionally: throw new InvalidOperationException or retry logic here
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Connected to Kogna at {_io.USBLocation()}");
                IsConnected = true;
            }

            // Pump accept loop
            Task.Run(AcceptClientsLoop, _cts.Token);
        }

        private async Task AcceptClientsLoop()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] KMotionServerHost.accept clients loop() called");

            while (!_cts.Token.IsCancellationRequested)
            {
                var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                try
                {
                    await server.WaitForConnectionAsync(_cts.Token);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Pipe.WaitForConnectionAsync returned – client is now connected!");
                }
                catch (OperationCanceledException)
                {
                    server.Dispose();
                    break;
                }

                _clientTasks.Add(Task.Run(() => HandleClientAsync(server), _cts.Token));
            }
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task HandleClientAsync(NamedPipeServerStream pipe)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            using var reader = new BinaryReader(pipe, Encoding.ASCII, leaveOpen: true);
            using var writer = new BinaryWriter(pipe, Encoding.ASCII, leaveOpen: true);

            try
            {
                IsConnected = true;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client connected.");

                while (pipe.IsConnected)
                {
                    // Read command code + board ID
                    int code;
                    try { code = reader.ReadInt32(); }
                    catch { break; }

                    int board = reader.ReadInt32();
                    int result = 0;
                    byte dest = 0; // DEST_NORMAL
                    var reply = new MemoryStream();
                    using var w = new BinaryWriter(reply, Encoding.ASCII, leaveOpen: true);
                    IsConnected = true;
                    switch ((Command)code)
                    {
                        case Command.WriteLineReadLine:
                            {
                                var cmd = ReadString(reader);
                                result = _io.WriteLineReadLine(board, cmd, out string resp);
                                w.Write(result);
                                w.Write(Encoding.ASCII.GetBytes(resp + "\0"));
                                break;
                            }
                        case Command.WriteLine:
                            {
                                var cmd = ReadString(reader);
                                result = _io.WriteLine(board, cmd);
                                w.Write(result);
                                break;
                            }
                        case Command.WriteLineWithEcho:
                            {
                                var cmd = ReadString(reader);
                                result = _io.WriteLineWithEcho(board, cmd);
                                w.Write(result);
                                break;
                            }
                        case Command.ReadLineTimeOut:
                            {
                                int timeout = reader.ReadInt32();
                                result = _io.ReadLineTimeOut(board, out string resp, timeout);
                                w.Write(result);
                                w.Write(Encoding.ASCII.GetBytes(resp + "\0"));
                                break;
                            }
                        case Command.Failed:
                            {
                                result = _io.Failed();
                                w.Write(result);
                                break;
                            }
                        case Command.Disconnect:
                            {
                                result = _io.Disconnect();
                                w.Write(result);
                                break;
                            }
                        case Command.FirmwareVersion:
                            {
                                result = _io.FirmwareVersion();
                                w.Write(result);
                                break;
                            }
                        case Command.USBLocation:
                            {
                                var loc = _io.USBLocation();
                                result = KognaIO.KOGNA_OK;
                                w.Write(result);
                                w.Write(Encoding.ASCII.GetBytes(loc + "\0"));
                                break;
                            }
                        case Command.KognaLock:
                            {
                                var caller = ReadString(reader);
                                result = _io.KognaLock(caller);
                                w.Write(result);
                                break;
                            }
                        case Command.KognaLockRecovery:
                            {
                                result = _io.KognaLockRecovery();
                                w.Write(result);
                                break;
                            }
                        case Command.ReleaseToken:
                            {
                                _io.ReleaseToken();
                                result = KognaIO.KOGNA_OK;
                                w.Write(result);
                                break;
                            }
                        case Command.ServiceConsole:
                            {
                                result = _io.ServiceConsole();
                                w.Write(result);
                                break;
                            }
                        case Command.CheckForReady:
                            {
                                result = _io.CheckForReady(board);
                                w.Write(result);
                                break;
                            }
                        default:
                            throw new InvalidOperationException($"Unknown command code: {code}");
                    }

                    // send back: dest, length, payload
                    byte[] payload = reply.ToArray();
                    writer.Write(dest);
                    writer.Write(payload.Length);
                    writer.Write(payload);
                    writer.Flush();
                }

            }
            catch
            {
                // swallow
            }
            finally
            {
                pipe.Disconnect();
                pipe.Dispose();

                IsConnected = false;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client disconnected.");
            }

        }

        private static string ReadString(BinaryReader r)
        {
            var sb = new StringBuilder();
            char ch;
            while ((ch = r.ReadChar()) != '\0')
                sb.Append(ch);
            return sb.ToString();
        }

        public void Dispose()
        {
            _cts.Cancel();
            Task.WaitAll(_clientTasks.ToArray(), TimeSpan.FromSeconds(2));
            _io.Dispose();
            
        }

        private enum Command
        {
            WriteLineReadLine   = 1,
            WriteLine           = 2,
            WriteLineWithEcho   = 3,
            ReadLineTimeOut     = 4,
            ListLocations       = 5,
            Failed              = 6,
            Disconnect          = 7,
            FirmwareVersion     = 8,
            USBLocation         = 9,
            KognaLock           = 10,
            KognaLockRecovery   = 11,
            ReleaseToken        = 12,
            ServiceConsole      = 13,
            CheckForReady       = 14
            // extend as needed...
        }
    }
}
