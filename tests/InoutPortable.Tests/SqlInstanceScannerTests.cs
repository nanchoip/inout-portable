using System.Text;
using InoutPortable.Core.Database;

namespace InoutPortable.Tests;

public class SqlInstanceScannerTests
{
    private static byte[] BuildResponse(string payload)
    {
        var data = Encoding.ASCII.GetBytes(payload);
        var buffer = new byte[3 + data.Length];
        buffer[0] = 0x05;
        buffer[1] = (byte)(data.Length & 0xFF);
        buffer[2] = (byte)((data.Length >> 8) & 0xFF);
        Array.Copy(data, 0, buffer, 3, data.Length);
        return buffer;
    }

    [Fact]
    public void Parses_multiple_instances_with_ports()
    {
        var payload =
            "ServerName;SRV1;InstanceName;A3ERP;IsClustered;No;Version;15.0.2000.5;tcp;49832;;" +
            "ServerName;SRV1;InstanceName;SQLEXPRESS;IsClustered;No;Version;15.0.2000.5;tcp;1433;;";

        var result = SqlInstanceScanner.ParseResponse(BuildResponse(payload), "192.168.30.83");

        Assert.Equal(2, result.Count);

        var a3 = result.Single(i => i.Instance == "A3ERP");
        Assert.Equal("192.168.30.83", a3.Server);
        Assert.Equal(49832, a3.TcpPort);
        Assert.Equal("15.0.2000.5", a3.Version);
        Assert.True(a3.IsA3Erp);

        Assert.Equal(1433, result.Single(i => i.Instance == "SQLEXPRESS").TcpPort);
    }

    [Fact]
    public void Ignores_invalid_datagram()
    {
        Assert.Empty(SqlInstanceScanner.ParseResponse(new byte[] { 0x00, 0x01 }, "h"));
        Assert.Empty(SqlInstanceScanner.ParseResponse(Array.Empty<byte>(), "h"));
    }
}
