-- =====================================================================
--  inout Portable — Script de tablas de DEMO para probar el importador
--  Ejecútalo en una base de datos de PRUEBAS de SQL Server.
--  Las hojas del Excel de ejemplo (ejemplo-import.xlsx) se llaman
--  igual que estas tablas: Clientes y Articulos.
-- =====================================================================

IF OBJECT_ID('dbo.Clientes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Clientes
    (
        Id      INT            NOT NULL PRIMARY KEY,   -- clave primaria (no identidad: el Excel trae el Id)
        Nombre  NVARCHAR(100)  NOT NULL,
        Email   NVARCHAR(150)  NULL,
        Saldo   DECIMAL(12, 2) NULL,
        Alta    DATE           NULL,
        Activo  BIT            NULL
    );
END;

IF OBJECT_ID('dbo.Articulos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Articulos
    (
        Codigo      NVARCHAR(20)   NOT NULL PRIMARY KEY,
        Descripcion NVARCHAR(200)  NULL,
        Precio      DECIMAL(12, 2) NULL,
        Stock       INT            NULL
    );
END;

-- Sugerencia de prueba:
--   1) Importa ejemplo-import.xlsx  -> deberían ser todo INSERT.
--   2) Vuelve a importar el mismo archivo -> deberían ser todo UPDATE.
--   3) Cambia algún valor en el Excel y reimporta -> UPDATE con el nuevo valor.
