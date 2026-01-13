using System;

namespace TorrentFileDecoder.Protocol;

public sealed class HandshakeCompleteEventArg(bool success, bool requiredAuth, string? msg) : EventArgs
{
    public bool Success => success;

    public bool RequireAuthentication => requiredAuth;

    public string? Message => msg;
}

public sealed class AuthenticationCompleteEventArg(bool success, string? msg) : EventArgs
{
    public bool Success => success;
    public string? Message => msg;
}