using System.Net;

namespace RamadanReliefAPI.Models;

public class ApiResponse
{
    public HttpStatusCode StatusCode { get; set; }

    public bool IsSuccess { get; set; }

    public string Message { get; set; }

    public object Result { get; set; }

    public List<string> Errors { get; set; }
}