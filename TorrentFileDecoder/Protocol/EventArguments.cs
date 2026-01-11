using System;

namespace TorrentFileDecoder.Protocol;

public sealed class HandshakeCompleteEventArg(bool success, string? msg) : EventArgs
{
    public bool Success => success;

    public string? Message => msg;
}