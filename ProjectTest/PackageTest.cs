using System.Collections;
using System.Diagnostics;
using System.Dynamic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Umi.Dht.Control.Protocol;
using Umi.Dht.Control.Protocol.Pack;

namespace ProjectTest;

public class PackageTest
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void TestAuthPayloadEncode()
    {
        var authPayload = new AuthPayload
        {
            UserName = "admin",
            Password = "a123",
        };
        var base64String = Convert.ToBase64String(authPayload.Encode(Encoding.ASCII).Span);
        Debug.Print(base64String);
        Debug.Assert("BQBhZG1pbgQAYTEyMw==" == base64String, "Encode Network Package Error");
    }

    [Test]
    public void TestAuthPayloadDecode()
    {
        var base64 = "BQBhZG1pbgQAYTEyMw==";
        var payload = AuthPayload.Decode(Convert.FromBase64String(base64), Encoding.ASCII);
        Debug.Assert(payload.UserName == "admin");

        Debug.Assert(payload.Password == "a123");
    }


    [Test]
    public void TestBasePack()
    {
        var size = Marshal.SizeOf<BasePack>();
        Debug.Print("Base Package size is {0}", size);
        Debug.Assert(size == 34);
        var session = Utils.GenerateSession();
        BasePack basePack = new()
        {
            Magic = Constants.MAGIC,
            Version = Constants.VERSION,
            Command = Constants.PING,
            Session = session.ToArray(),
            Length = 0
        };
        var encode = basePack.Encode();
        BasePack.Decode(encode, out var result);
        Debug.Assert(basePack.Session.SequenceEqual(result.Session));
    }

    [Test]
    public void TestAuthResponse()
    {
        var s = new TorrentResponse()
        {
            Result = TorrentResponse.GetErrorCode(false, 0x123456),
            Error = "abcde"
        };
        Debug.Assert(!s.IsSuccess, "success compute error");
        Debug.Assert(s.ErrorCode == 0x123456, "error code error");
        var data = s.Encode(Encoding.ASCII);
        var p = TorrentResponse.Decode(data, Encoding.ASCII);
        Debug.Assert(p.Result == s.Result, "result code error");
        Debug.Assert(p.Error == s.Error, "error message error");
    }

    [Test]
    public void FunctionProgramTest()
    {
        Func<IEnumerable<long>> fib = () => Enumerable.InfiniteSequence(0, 1)
            .Select(m => (((Func<Func<Func<int, long>, Func<int, long>>, Func<int, long>>)(g =>
                ((SelfHandler<Func<int, long>>)(x => n => g(x(x))(n)))(x => n => g(x(x))(n))))(f =>
                x => x < 2 ? 1 : f(x - 1) + f(x - 2)))(m));
        StringBuilder sb = new();
        sb.AppendJoin(',', fib().Take(10));
        Assert.Warn($"Result: {sb}");
    }

    private delegate T SelfHandler<T>(SelfHandler<T> self);

    [Test]
    public void BinaryFormatTest()
    {
        int input = 0x12345678;
        StringBuilder sb = new();
        for (int i = 31; i >= 0; i--)
        {
            sb.Append(((input & (1 << i)) == (1 << i)) ? '1' : '0');
        }

        Debug.Assert(input.ToString("b32") == sb.ToString());
    }
}