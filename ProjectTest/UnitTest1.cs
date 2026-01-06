using System.Diagnostics;
using System.Text;
using Umi.Dht.Control.Protocol.Pack;

namespace ProjectTest;

public class Tests
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
}