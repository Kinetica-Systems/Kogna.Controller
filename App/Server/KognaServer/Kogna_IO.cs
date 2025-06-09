// CKMotionIO.cs: C# port of the CKMotionIO class (network-only)
// Removed FTDI logic; uses TCP socket for communication

using System;

using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.Text;
using System.IO;


namespace KognaServer.Server.KognaServer
{
    public delegate int ServerConsoleHandler(int board, string buf);

    public class KognaIO : IDisposable
    {
        // Public fields and properties
        public bool FailMessageAlreadyShown;
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
        private Socket _socket = null!;
        private Mutex _mutex;
        private Stopwatch _timer;
        private int _token;
        private ServerConsoleHandler _consoleHandler = null!;


        public KognaIO(string ipAddress, int port)
        {
            IPAddress = ipAddress;
            Port = port;
            _mutex = new Mutex(false, nameof(KognaIO));
            _timer = new Stopwatch();

            // Default settings
            SendAbortOnConnect = true;
            FailMessageAlreadyShown = false;
            NonRespondingCount = 0;
            _token = 0;
            Connected = false;
        }

        public void Dispose()
        {
            _socket?.Close();
            _mutex?.Dispose();
        }

        // Connect to Kogna over TCP
        public int Connect()
        {
            if (NonRespondingCount >= CONNECT_TRIES)
                return KOGNA_ERROR;

            _mutex.WaitOne();
            try
            {
                if (!RequestedDeviceAvail(out var reason))
                {
                    ErrorMessage(reason);
                    return KOGNA_ERROR;
                }

                _timer.Restart();
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    Blocking = true  // switch to blocking mode
                };

                var endPoint = new IPEndPoint(System.Net.IPAddress.Parse(IPAddress), Port);
                try
                {
                    // This will now block up to the OS TCP timeout (or you can use BeginConnect/ConnectAsync+WaitHandle)
                    _socket.Connect(endPoint);
                    Connected = true;
                    NonRespondingCount = 0;

                }
                catch (SocketException ex)
                {
                    ErrorMessage("Unable to connect: " + ex.Message);
                    return KOGNA_ERROR;
                }

                return KOGNA_OK;
            }
            finally
            {
                _mutex.ReleaseMutex();

            }

        }

        public int Disconnect()
        {
            _mutex.WaitOne();
            try
            {
                Connected = false;
                _socket?.Close();
                return KOGNA_OK;
            }
            finally { _mutex.ReleaseMutex(); }
        }

        public int Failed()
        {
            _mutex.WaitOne();
            try
            {
                Connected = false;
                _socket?.Close();
                if (!FailMessageAlreadyShown)
                {
                    ReleaseToken();
                    ErrorMessage("Read Failed - Auto Disconnect");
                }
                FailMessageAlreadyShown = true;
                return KOGNA_OK;
            }
            finally { _mutex.ReleaseMutex(); }
        }

        // Firmware and location info
        public int FirmwareVersion()
        {
            Console.WriteLine("version call");
                // send the literal Version command over TCP
            if (WriteLineReadLine(0, "Version", out var resp) != KOGNA_OK)
                return KOGNA_ERROR;
                // parse the numeric portion of the reply
                return int.TryParse(resp.Trim(), out var v) ? v : KOGNA_ERROR;


        }
        public string USBLocation() => Connected ? $"{IPAddress}:{Port}" : "Not Connected";

        // Locking
        public int KognaLock(string callerID)
        {
            if (!_mutex.WaitOne(3000)) return KOGNA_NOT_CONNECTED;
            try
            {
                if (!Connected)
                {
                    if (Connect() != KOGNA_OK) return KOGNA_NOT_CONNECTED;
                }
                if (_token == 0)
                {
                    _token++;
                    LastCallerID = callerID ?? string.Empty;
                    return KOGNA_LOCKED;
                }
                else return KOGNA_IN_USE;
            }
            finally { _mutex.ReleaseMutex(); }
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
            _mutex.WaitOne();
            try
            {
                LastCallerID = string.Empty;
                _token--;
                if (_token < 0) _token = 0;
            }
            finally { _mutex.ReleaseMutex(); }
        }

        public int MakeSureConnected()
        {
            return Connected ? KOGNA_OK : Connect();
            
        }

        // I/O
        public int WriteLine(int board, string buf)
        {
            if (!Connected) return KOGNA_NOT_CONNECTED;
            var data = Encoding.ASCII.GetBytes(buf + "\r");
            try { _socket.Send(data); return KOGNA_OK; }
            catch { return KOGNA_ERROR; }
        }

        public int ReadLine(int board, out string buf)
        {
            buf = string.Empty;
            if (!Connected) return KOGNA_NOT_CONNECTED;
            try
            {
                var sb = new StringBuilder();
                var buffer = new byte[1];
                while (true)
                {
                    if (_socket.Receive(buffer) <= 0) break;
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
    _mutex.WaitOne();
    try
    {
        // 1) Make sure we’re still connected
        if (!Connected) { response = ""; return KOGNA_NOT_CONNECTED; }

        // 2) Flush any stray bytes on the socket
        while (_socket.Available > 0)
            _socket.Receive(new byte[_socket.Available]);

        // 3) Trim off any CR/LF/NUL the caller may have left on
        send = send.TrimEnd('\r', '\n', '\0');

        // 4) Build one contiguous packet: ESC,01 + ASCII + CR
        //    This is exactly what CKMotionIO::WriteLine does under the hood.
        var cmd = "\x1B\x01" + send + "\r";   
        var data = Encoding.ASCII.GetBytes(cmd);
        _socket.Send(data);

        // 5) Now read back until '\n', dropping any leading ESC or CR
        var sb = new StringBuilder();
        var one = new byte[1];
        while (_socket.Receive(one, 1, SocketFlags.None) == 1)
        {
            char c = (char)one[0];
            if (c == '\n')      // end-of-line
                break;
            if (c < ' ')  
                continue;        // skip CR and any ESC prefixes
            sb.Append(c);
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
        _mutex.ReleaseMutex();
    }
}


        public int WriteLineWithEcho(int board, string s)
        {
            if (WriteLine(board, s) != KOGNA_OK) return KOGNA_ERROR;
            return ReadLine(board, out _);
        }

        public int FlushInputBuffer()
        {
            if (!Connected) return KOGNA_NOT_CONNECTED;
            try
            {
                while (_socket.Available > 0)
                {
                    var dummy = new byte[_socket.Available];
                    _socket.Receive(dummy);
                }
                return KOGNA_OK;
            }
            catch { return KOGNA_ERROR; }
        }

        public int FlushInputBufferKogna() => FlushInputBuffer();

        public int NumberBytesAvailToRead(out int navail, bool showMessage)
        {
            navail = Connected ? _socket.Available : 0;
            return KOGNA_OK;
        }

        public int ReadBytesAvailable(int board, byte[] rxBuffer, int maxbytes, out int bytesReceived, int timeoutMs)
        {
            bytesReceived = 0;
            if (!Connected) return KOGNA_NOT_CONNECTED;
            var sw = Stopwatch.StartNew();
            try
            {
                while (_socket.Available == 0 && sw.ElapsedMilliseconds < timeoutMs)
                    Thread.Sleep(1);
                bytesReceived = _socket.Receive(rxBuffer, 0, Math.Min(maxbytes, _socket.Available), SocketFlags.None);
                return KOGNA_OK;
            }
            catch { return KOGNA_ERROR; }
        }

        public int WriteLineReadLine(int board, string send)
        {
            // overload without response
            return WriteLineReadLine(board, send, out _);
        }

        public int ReadSendNextLine(int board, StreamReader reader)
        {
            if (!Connected) return KOGNA_NOT_CONNECTED;
            var line = reader.ReadLine();
            if (line != null) return WriteLine(board, line);
            return KOGNA_OK;
        }

        public int HandleDiskIO(int board, string filePath)
        {
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
        public int SetConsoleCallback(ServerConsoleHandler handler)
        {
            _consoleHandler = handler;
            return KOGNA_OK;
        }

        public int LogToConsole(int board, string s)
        {
            _consoleHandler?.Invoke(board, s);
            return KOGNA_OK;
        }

        public int ServiceConsole()
        {
            if (!Connected) return KOGNA_NOT_CONNECTED;
            try
            {
                while (_socket.Available > 0)
                {
                    if (ReadLine(0, out string line) == KOGNA_OK)
                        _consoleHandler?.Invoke(0, line);
                    else break;
                }
                return KOGNA_OK;
            }
            catch { return KOGNA_ERROR; }
        }

        public int CheckForReady(int board)
        {
            if (ServiceConsole() != KOGNA_OK) return KOGNA_TIMEOUT;
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
                    if (_socket.Available > 0)
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
