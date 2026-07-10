using InoutPortable.Core.Database;

namespace InoutPortable.Tests;

public class SistemaIniReaderTests
{
    [Fact]
    public void Parses_server_and_system_database()
    {
        var ini = "[Conexion]\r\nServidor=A3ERP\\A3ERP\r\nBaseDatos=A3ERP$SISTEMA\r\nOtra=cosa\r\n";
        var info = SistemaIniReader.Parse(ini);
        Assert.Equal("A3ERP\\A3ERP", info.Server);
        Assert.Equal("A3ERP$SISTEMA", info.SystemDatabase);
        Assert.True(info.HasAny);
    }

    [Fact]
    public void Is_case_insensitive_and_ignores_comments_and_sections()
    {
        var ini = "; comentario\n[SECCION]\nSERVIDOR = 192.168.30.83\\A3ERP \n  basedatos = MI$SISTEMA \n";
        var info = SistemaIniReader.Parse(ini);
        Assert.Equal("192.168.30.83\\A3ERP", info.Server);
        Assert.Equal("MI$SISTEMA", info.SystemDatabase);
    }

    [Fact]
    public void Returns_empty_when_keys_absent()
    {
        var info = SistemaIniReader.Parse("[x]\nfoo=bar\n");
        Assert.Null(info.Server);
        Assert.Null(info.SystemDatabase);
        Assert.False(info.HasAny);
    }
}
