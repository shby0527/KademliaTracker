using System.Diagnostics;
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
        Debug.WriteLine(base64String);
        Assert.That(base64String, Is.EqualTo("BQBhZG1pbgQAYTEyMw=="), "Encode Network Package Error");
    }

    [Test]
    public void TestAuthPayloadDecode()
    {
        var base64 = "BQBhZG1pbgQAYTEyMw==";
        var payload = AuthPayload.Decode(Convert.FromBase64String(base64), Encoding.ASCII);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(payload.UserName, Is.EqualTo("admin"), $"{payload.UserName} not equal admin");
            Assert.That(payload.Password, Is.EqualTo("a123"), $"{payload.Password} not equal a123");
        }
    }


    [Test]
    public void TestBasePack()
    {
        var size = Marshal.SizeOf<BasePack>();
        Debug.Print("Base Package size is {0}", size);
        Assert.That(size, Is.EqualTo(34), $"{size} not equal 34");
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
        Assert.That(basePack.Session, Is.EquivalentTo(result.Session),
            $"{basePack.Session} not equal {result.Session}");
    }

    [Test]
    public void TestAuthResponse()
    {
        var s = new TorrentResponse()
        {
            Result = TorrentResponse.GetErrorCode(false, 0x123456),
            Error = "abcde"
        };
        using (Assert.EnterMultipleScope())
        {
            Assert.That(s.IsSuccess, Is.False, "success compute error");
            Assert.That(s.ErrorCode, Is.EqualTo(0x123456), "error code error");
        }

        var data = s.Encode(Encoding.ASCII);
        var p = TorrentResponse.Decode(data, Encoding.ASCII);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(p.Result, Is.EqualTo(s.Result), "result code error");
            Assert.That(p.Error, Is.EqualTo(s.Error), "error message error");
        }
    }

    private static IEnumerable<long> Fibonacci()
    {
        yield return 1;
        yield return 1;
        var next = Fibonacci().Zip(Fibonacci().Skip(1), (a, b) => a + b);
        foreach (var l in next)
        {
            yield return l;
        }
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
        sb.Clear();
        sb.AppendJoin(',', Fibonacci().Take(10));
        Assert.Warn($"Other Result: {sb}");
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
        Span<byte> data = stackalloc byte[20];
        using var fs = File.OpenRead(@"/dev/random");
        fs.ReadExactly(data);
        sb.Clear();
        sb.AppendJoin(',', data.ToArray());
        Assert.Warn($"Result: {sb}");
    }
}