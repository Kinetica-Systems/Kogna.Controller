// CKMotionIO.cs: C# port of the CKMotionIO class (network-only)
// Removed FTDI logic; uses TCP socket for communication

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace TCPServer
{
    public delegate int ServerConsoleHandler(int board, string buf);

    public class KognaIO : IKognaIO
    {
        // Private fields
        private bool _failMessageAlreadyShown;
        private bool _sendAbortOnConnect;
        private int _nonRespondingCount;
        private ServerConsoleHandler? _consoleHandler;
        
        // Explicit interface implementation
        bool IKognaIO.FailMessageAlreadyShown { get => _failMessageAlreadyShown; set => _failMessageAlreadyShown = value; }
        bool IKognaIO.SendAbortOnConnect { get => _sendAbortOnConnect; set => _sendAbortOnConnect = value; }
        int IKognaIO.NonRespondingCount { get => _nonRespondingCount; set => _nonRespondingCount = value; }
        public bool SendAbortOnConnect;
        public int NonRespondingCount;
        public bool BoardIDAssigned { get; set; }
        private string IPAddress { get; }
        private int Port { get; }
        public bool Connected { get; set; }
        public string ErrMsg { get; set; } = string.Empty;
        public string LastCallerID { get; private set; } = string.Empty;

        // Internal synchronization and state
        private const int CONNECT_TRIES = 5;
        private const double TIME_TO_TRY_TO_OPEN = 3.0;
        private Socket? _socket;
        private Mutex? _mutex;
        private readonly Stopwatch _timer;
        private int _token;
        private const int DEFAULT_CONNECT_TIMEOUT = 10; // seconds
        private readonly int _connectTimeout;
        private bool _disposed;

        public KognaIO(string ipAddress, int port, int connectTimeout = DEFAULT_CONNECT_TIMEOUT)
        {
            IPAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            Port = port;
            _connectTimeout = connectTimeout;
            _mutex = new Mutex();
            _timer = new Stopwatch();
            Connected = false;
            _nonRespondingCount = 0;
            _failMessageAlreadyShown = false;
            _sendAbortOnConnect = false;
            _disposed = false;
        }

        public void SetConsoleCallback(ServerConsoleHandler handler)
        {
            ThrowIfDisposed();
            _consoleHandler = handler;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    if (_socket != null)
                    {
                        try
                        {
                            if (_socket.Connected)
                            {
                                _socket.Shutdown(SocketShutdown.Both);
                            }
                            _socket.Close();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error during socket cleanup: {ex.Message}");
                        }
                        _socket = null;
                    }

                    if (_mutex != null)
                    {
                        _mutex.Dispose();
                        _mutex = null;
                    }
                }

                // Clean up unmanaged resources (if any) here
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~KognaIO()
        {
            Dispose(false);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(KognaIO));
            }
        }

        // Add this method to ensure thread-safe access to the socket
        private void EnsureSocketConnected()
        {
            ThrowIfDisposed();
            if (_socket == null || !_socket.Connected)
            {
                throw new InvalidOperationException("Socket is not connected");
            }
        }

        public int Connect()
        {
            ThrowIfDisposed();
            try
            {
                _mutex?.WaitOne();
                try
                {
                    if (_socket != null)
                    {
                        try
                        {
                            if (_socket.Connected)
                            {
                                _socket.Shutdown(SocketShutdown.Both);
                            }
                            _socket.Close();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error closing existing socket: {ex.Message}");
                            return KOGNA_ERROR;
                        }
                    }

                    _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    var endpoint = new IPEndPoint(System.Net.IPAddress.Parse(IPAddress), Port);
                    
                    var connectResult = _socket.BeginConnect(endpoint, null, null);
                    bool success = connectResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(_connectTimeout));
                    
                    if (!success)
                    {
                        _socket.Close();
                        ErrMsg = "Connection attempt timed out";
                        return KOGNA_ERROR;
                    }

                    _socket.EndConnect(connectResult);
                    Connected = true;
                    return KOGNA_OK;
                }
                finally
                {
                    _mutex?.ReleaseMutex();
                }
            }
            catch (Exception ex)
            {
                ErrMsg = $"Connection failed: {ex.Message}";
                Connected = false;
                return KOGNA_ERROR;
            }
        }

        public int Disconnect()
        {
            _mutex?.WaitOne();
            try
            {
                Connected = false;
                _socket?.Close();
                return KOGNA_OK;
            }
            finally { _mutex?.ReleaseMutex(); }
        }

        public int Failed()
        {
            _mutex?.WaitOne();
            try
            {
                Connected = false;
                _socket?.Close();
                if (!_failMessageAlreadyShown)
                {
                    ReleaseToken();
                    ErrorMessage("Read Failed - Auto Disconnect");
                }
                _failMessageAlreadyShown = true;
                return KOGNA_OK;
            }
            finally { _mutex?.ReleaseMutex(); }
        }

        
        public string USBLocation() => Connected ? $"{IPAddress}:{Port}" : "Not Connected";

        // Locking
        public int KognaLock(string callerID)
        {
            ThrowIfDisposed();
            if (!_mutex?.WaitOne(3000) ?? false) return KOGNA_NOT_CONNECTED;
            try
            {
                if (!Connected)
                {
                    if (Connect() != KOGNA_OK) return KOGNA_NOT_CONNECTED;
                }
                if (_token == 0)
                {
                    _token = 1;
                    LastCallerID = callerID;
                    return KOGNA_OK;
                }
                else return KOGNA_IN_USE;
            }
            finally { _mutex?.ReleaseMutex(); }
        }

        public int KognaLockRecovery()
        {
            SendAbortOnConnect = false;
            var res = KognaLock("KognaLockRecovery");
            SendAbortOnConnect = true;
            return res;
        }

        public void ReleaseToken()
        {
            _mutex?.WaitOne();
            try
            {
                LastCallerID = string.Empty;
                _token--;
                if (_token < 0) _token = 0;
            }
            finally { _mutex?.ReleaseMutex(); }
        }

        public int MakeSureConnected()
        {
            if (Connected) 
                return KOGNA_OK;
                
            int result = Connect();
            return result == KOGNA_OK ? KOGNA_OK : KOGNA_ERROR;
        }

        // I/O
        public int WriteLine(int board, string buf)
        {
            ThrowIfDisposed();
            if (!Connected) return KOGNA_NOT_CONNECTED;
            var data = Encoding.ASCII.GetBytes(buf + "\r");
            try { _socket?.Send(data); return KOGNA_OK; }
            catch { return KOGNA_ERROR; }
        }

        public int ReadLine(int board, out string buf)
        {
            buf = string.Empty;
            ThrowIfDisposed();
            if (!Connected) return KOGNA_NOT_CONNECTED;
            try
            {
                var sb = new StringBuilder();
                var buffer = new byte[1];
                while (true)
                {
                    if (_socket?.Receive(buffer) <= 0) break;
                    char c = (char)buffer[0];
                    if (c == '\n') break;
                    sb.Append(c);
                }
                buf = sb.ToString().TrimEnd('\r');
                return KOGNA_OK;
            }
            catch { return KOGNA_ERROR; }
        }



public int WriteLineReadLine(int board, string send, out string response)
{
    ThrowIfDisposed();
    _mutex?.WaitOne();

    try
            {
                // 1) Make sure we’re still connected
                if (!Connected) { response = ""; return KOGNA_NOT_CONNECTED; }
                // 2) Flush any stray bytes on the socket
                while (_socket?.Available > 0)
                    _socket.Receive(new byte[_socket.Available]);

                // 3) Trim off any CR/LF/NUL the caller may have left on
                send = send.TrimEnd('\r', '\n', '\0');

                // 4) Build one contiguous packet: ESC,01 + ASCII + CR
                //    This is exactly what CKMotionIO::WriteLine does under the hood.
                var cmd = "\x1B\x01" + send + "\r";
                var data = Encoding.ASCII.GetBytes(cmd);
                _socket?.Send(data);

                // 5) Now read back until '\n', dropping any leading ESC or CR
                var sb = new StringBuilder();
                var one = new byte[1];
                var sw = Stopwatch.StartNew();
                const int timeoutMs = 5000; // 5 second timeout
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    // Check if data is available before blocking
                    if (_socket?.Available > 0)
                    {
                        if (_socket.Receive(one, 1, SocketFlags.None) == 1)
                        {
                            char c = (char)one[0];
                            if (c == '\n')      // end-of-line
                                break;
                            if (c < ' ')
                                continue;        // skip CR and any ESC prefixes
                            sb.Append(c);
                        }
                    }
                    else
                    {
                        // No data available, wait a bit before checking again
                        Thread.Sleep(10);
                    }
                }
                
                if (sw.ElapsedMilliseconds >= timeoutMs)
                {
                    Console.WriteLine($"[KOGNA_IO] WriteLineReadLine timeout after {timeoutMs}ms for command: {send}");
                    response = "";
                    return KOGNA_TIMEOUT;
                }
                
                response = sb.ToString();
                return KOGNA_OK;
            }
            catch (SocketException ex)
            {
                ErrorMessage($"WriteLineReadLine socket error: {ex.Message}");
                response = "";
                return KOGNA_ERROR;
            }
            finally
            {
                _mutex?.ReleaseMutex();
            }
}


        public int WriteLineWithEcho(int board, string s)
        {
            if (WriteLine(board, s) != KOGNA_OK) return KOGNA_ERROR;
            return ReadLine(board, out _);
        }

        public int FlushInputBuffer()
        {
            ThrowIfDisposed();
            if (!Connected) return KOGNA_NOT_CONNECTED;
            try
            {
                while (_socket?.Available > 0)
                {
                    var dummy = new byte[_socket.Available];
                    _socket.Receive(dummy);
                }
                return KOGNA_OK;
            }
            catch { return KOGNA_ERROR; }
        }

        public int NumberBytesAvailToRead(out int navail, bool showMessage)
        {
            navail = Connected ? _socket?.Available ?? 0 : 0;
            return KOGNA_OK;
        }

        public int ReadBytesAvailable(int board, byte[] rxBuffer, int maxbytes, out int bytesReceived, int timeoutMs)
        {
            bytesReceived = 0;
            ThrowIfDisposed();
            if (!Connected) return KOGNA_NOT_CONNECTED;
            var sw = Stopwatch.StartNew();
            try
            {
                while (_socket?.Available == 0 && sw.ElapsedMilliseconds < timeoutMs)
                    Thread.Sleep(1);
                bytesReceived = _socket?.Receive(rxBuffer, 0, Math.Min(maxbytes, _socket.Available), SocketFlags.None) ?? 0;
                return KOGNA_OK;
            }
            catch { return KOGNA_ERROR; }
        }

        public int ReadSendNextLine(int board, StreamReader reader)
        {
            ThrowIfDisposed();
            if (!Connected) return KOGNA_NOT_CONNECTED;
            var line = reader.ReadLine();
            if (line != null) return WriteLine(board, line);
            return KOGNA_OK;
        }

        public int HandleDiskIO(int board, string filePath)
        {
            ThrowIfDisposed();
            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    int result = 0;
                    while (!reader.EndOfStream)
                        result = ReadSendNextLine(board, reader);
                    return result;
                }
            }
            catch
            {
                return WriteLine(board, "ReadDiskData 2 0");
            }
        }

        // Console callback and service
        public int LogToConsole(int board, string s)
        {
            ThrowIfDisposed();
            _consoleHandler?.Invoke(board, s);
            return KOGNA_OK;
        }

        public void ServiceConsole()
        {
            try
            {
                ThrowIfDisposed();
                if (!Connected) return;
                
                while (_socket?.Available > 0)
                {
                    if (ReadLine(0, out string line) == KOGNA_OK)
                        _consoleHandler?.Invoke(0, line);
                    else break;
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't throw since this is a void method
                Console.WriteLine($"Error in ServiceConsole: {ex.Message}");
            }
        }

        public int CheckForReady(int board)
        {
            ThrowIfDisposed();
            ServiceConsole(); // No return value to check
            if (ReadLine(board, out string line) != KOGNA_OK) return KOGNA_TIMEOUT;
            var lower = line.ToLowerInvariant();
            if (lower.Contains("error")) return KOGNA_ERROR;
            if (lower.Contains("ok") || lower.Contains("ready")) return KOGNA_READY;
            return KOGNA_OK;
        }
        /// <summary>
        /// Reads a line from the Kogna device, waiting up to <paramref name="timeoutMs"/> milliseconds.
        /// Returns KOGNA_OK, KOGNA_TIMEOUT, or KOGNA_ERROR, and outputs the response when OK.
        /// </summary>
        /// <summary>
        /// Reads a line from the device, waiting up to <paramref name="timeoutMs"/> milliseconds.
        /// Returns one of KOGNA_OK, KOGNA_TIMEOUT, or KOGNA_ERROR, and outputs the text when OK.
        /// </summary>
        public int ReadLineTimeOut(int board, out string response, int timeoutMs = 20000)
        {
            ThrowIfDisposed();
            response = string.Empty;

            // Ensure we have a live connection
            int rc = MakeSureConnected();
            if (rc != KOGNA_OK)
                return rc;

            var sw = Stopwatch.StartNew();
            try
            {
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (_socket?.Available > 0)
                    {
                        int r = ReadLine(board, out response);
                        if (r != KOGNA_OK)
                        {
                            Connected = false;      // mark disconnected on error
                            return KOGNA_ERROR;
                        }
                        return KOGNA_OK;
                    }
                    Thread.Sleep(5);
                }

                // timeout expired
                return KOGNA_TIMEOUT;
            }
            catch (SocketException)
            {
                Connected = false;
                return KOGNA_ERROR;
            }
}


        private bool RequestedDeviceAvail(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public void ErrorMessage(string msg)
        {
            ErrMsg = msg;
        }

        // Constants for lock/results
        public const int KOGNA_OK = 0;
        public const int KOGNA_TIMEOUT = 1;
        public const int KOGNA_ERROR = 2;
        public const int KOGNA_READY = 3;
        public const int KOGNA_LOCKED = 4;
        public const int KOGNA_IN_USE = 5;
        public const int KOGNA_NOT_CONNECTED = 6;
    }
}
