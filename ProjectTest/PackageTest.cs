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
        var base64String = Convert.ToBase64String(authPayload.Encode(Encoding.ASCII));
        Debug.Print(base64String);
        Debug.Assert("BQBhZG1pbgQAYTEyMw==" == base64String, "Encode Network Package Error");
    }

    [Test]
    public void TestAuthPayloadDecode()
    {
        var base64 = "BQBhZG1pbgQAYTEyMw==";
        var payload = AuthPayload.Decode(Convert.FromBase64String(base64), Encoding.ASCII);
        Debug.Assert(payload.UserName == "admin");
        Debug.Assert(payload.UserNameLength == 5);
        Debug.Assert(payload.Password == "a123");
        Debug.Assert(payload.PasswordLength == 4);
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
}