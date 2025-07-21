using System;
using System.Threading.Tasks;
using KognaComms;

namespace KognaServer.Models;

/// <summary>
/// Client for communicating with the server via IPC
/// </summary>
public class AppIpcClient
{
    private readonly KognaControl? _server;

    public AppIpcClient(KognaControl? server)
    {
        _server = server;
    }

    /// <summary>
    /// Sends a command to the server and returns the response
    /// </summary>
    public async Task<IpcResponse> SendCommandAsync(string command)
    {
        try
        {
            if (_server == null)
            {
                return new IpcResponse
                {
                    Status = "Error",
                    Result = string.Empty,
                    Error = "Server not connected"
                };
            }

            var (response, result) = await _server.ProcessIpcCommand(command);
            return new IpcResponse
            {
                Status = response.Contains("Error") ? "Error" : "OK",
                Result = result,
                Error = response.Contains("Error") ? response : string.Empty
            };
        }
        catch (Exception ex)
        {
            return new IpcResponse
            {
                Status = "Error",
                Result = string.Empty,
                Error = ex.Message
            };
        }
    }
}

/// <summary>
/// Response from an IPC command
/// </summary>
public class IpcResponse
{
    public string Status { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}
