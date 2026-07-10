# inout Portable — Importador de Excel a Microsoft SQL Server

Herramienta de escritorio para Windows que replica el concepto **inout de a3ERP**: cargas un
archivo **Excel** donde **cada hoja = una tabla** de la base de datos y **la primera fila = los
nombres de columna**, y la aplicación **inserta/actualiza (upsert)** esos datos en **Microsoft
SQL Server**, validando todo antes de escribir y dentro de una **transacción con rollback**.

El resultado se entrega como **un único `.exe` portable** que se ejecuta con **doble click**,
sin instalar .NET, sin configurar servidores y **sin necesidad del driver ODBC** de SQL Server.

---

## 1. Por qué .NET / C# (y no PHP/Laravel)

El objetivo es un artefacto "doble click" autónomo. El punto crítico es la conexión a SQL Server:

- **PHP/Laravel** necesita `pdo_sqlsrv`, que **depende del ODBC Driver de Microsoft** instalado en
  la máquina destino. Eso rompe el ideal de "un solo ejecutable sin instalar nada".
- **.NET** usa **`Microsoft.Data.SqlClient`**, un **driver TDS puro y gestionado** que habla con
  SQL Server directamente por TCP: **no requiere ODBC**. Además ofrece el mejor soporte de
  metadatos (claves primarias, tipos, longitudes), transacciones e `IDENTITY_INSERT`.

Publicando con `--self-contained` + `PublishSingleFile`, todo el runtime de .NET queda **embebido
dentro del `.exe`**. La máquina destino solo necesita **Windows x64**.

---

## 2. Arquitectura

Solución con separación limpia de responsabilidades:

```
InoutPortable.sln
├─ src/
│  ├─ InoutPortable.Core/           (biblioteca de dominio, sin UI)
│  │  ├─ Excel/ExcelWorkbookReader.cs        Lee .xls y .xlsx (ExcelDataReader) por contenido
│  │  ├─ Excel/SheetInterpreter.cs           Autodetecta la fila de cabecera (casa con columnas reales)
│  │  ├─ Database/
│  │  │   ├─ ConnectionSettings.cs           Parámetros de conexión + cadena de conexión
│  │  │   ├─ ConnectionTester.cs             "Probar conexión" (SELECT @@VERSION)
│  │  │   ├─ SqlInstanceScanner.cs           Descubre instancias A3ERP en la red (SSRP/UDP 1434)
│  │  │   └─ SqlServerMetadataProvider.cs    Resuelve tablas, columnas, tipos y PK (INFORMATION_SCHEMA/sys)
│  │  ├─ Validation/
│  │  │   ├─ TypeConverter.cs                Convierte/valida cada celda contra el tipo SQL
│  │  │   └─ (reglas de longitud, rango, fecha, bit, decimal, GUID…)
│  │  ├─ Import/
│  │  │   ├─ KeyNormalizer.cs                Canonicaliza claves (Excel ⇆ SQL) para decidir insert/update
│  │  │   ├─ ImportPlanner.cs                Clasifica cada fila: Insert / Update / Error
│  │  │   ├─ SqlExistingKeyLookup.cs         Consulta por lotes qué claves ya existen
│  │  │   ├─ UpsertExecutor.cs               Ejecuta INSERT/UPDATE en una transacción (rollback total)
│  │  │   └─ ImportOrchestrator.cs           Orquesta: leer → resolver → validar → previsualizar → ejecutar
│  │  ├─ Settings/SettingsStore.cs           Persiste la conexión (contraseña cifrada con DPAPI)
│  │  ├─ Logging/ImportLog.cs                Historial de importaciones (JSONL)
│  │  └─ Infrastructure/AppPaths.cs          Rutas de datos portables (junto al .exe)
│  └─ InoutPortable.App/            (aplicación WPF de escritorio)
│      ├─ MainWindow.xaml(.cs)              Wizard: Conexión → Importar → Historial
│      └─ GridRows.cs                        DTOs para las tablas de la interfaz
├─ tests/InoutPortable.Tests/       (xUnit: 29 pruebas)
├─ tools/SampleGenerator/           (genera el Excel de ejemplo)
├─ samples/                         (ejemplo-import.xlsx + crear-tablas-demo.sql)
└─ build/publish.ps1                (empaqueta el .exe portable)
```

### Flujo funcional (las 7 fases del encargo)

1. **Lectura de Excel (.xls y .xlsx)** — `ExcelWorkbookReader` (basado en **ExcelDataReader**) lee
   tanto el formato moderno `.xlsx` como el **antiguo `.xls`** de las plantillas de a3ERP,
   **detectando el formato por el contenido** (funciona aunque la extensión esté equivocada).
   Recorre todas las hojas; nombre de hoja = tabla, cabecera = columnas, resto = datos. Conserva
   tipos (número, fecha, texto, bit) y el número de fila de Excel para los mensajes de error. Salta
   filas y hojas vacías.
   - **Autodetección de la fila de cabecera** (`SheetInterpreter`): muchas plantillas de a3ERP
     tienen una fila de **etiquetas en español** encima de la fila con los **códigos de campo**
     (`CODART`, `DESCART`, `CODCLI`…). La app elige automáticamente la fila cuyos nombres coinciden
     con las columnas reales de la tabla destino. Se puede forzar manualmente.
   - **Selección de hojas**: en el resumen puedes marcar/desmarcar qué hojas importar; las hojas
     auxiliares cuyo nombre no corresponde a una tabla se descartan solas.
2. **Conexión configurable** — pantalla de ajustes con host, instancia, puerto, BBDD, usuario y
   contraseña (o autenticación de Windows). Botón **Probar conexión**. Se guarda de forma
   persistente con la **contraseña cifrada (DPAPI, por usuario de Windows)**.
   - **Buscar instancias A3ERP** — botón que descubre servidores SQL Server en la red mediante el
     SQL Server Browser (UDP 1434, protocolo SSRP): difusión en la subred local, barrido del /24 y
     consulta a una IP concreta. Como todas las instancias de a3ERP se llaman `A3ERP`, se muestran
     primero; al elegir una, se rellenan automáticamente el host y el puerto (`SqlInstanceScanner`).
   - **Elegir empresa a3ERP** — réplica fiel del gestor de empresas nativo (sin el BPL de a3ERP): lee
     la base de datos de sistema (`…$SISTEMA`, autodetectada) y lista las **empresas** (tabla
     `EMPRESAS`) con **logo**, **buscador en vivo** y **filtro opcional por usuario de a3ERP**
     (`__EMPRESASUSUARIO`, igual que la consulta nativa). Al elegir una, fija su base de datos
     (`DATABASENAME`) y su servidor (`SERVERNAME`). Además, **"Cargar servidor de a3ERP (Sistema.ini)"**
     lee el `Sistema.ini` del a3ERP instalado para prerrellenar servidor y BD de sistema, como hace el
     inout original al arrancar (`A3ErpCompanyProvider`, `SistemaIniReader`).
     - *Nota:* no se puede reutilizar el formulario nativo del BPL (requiere todo el runtime de a3ERP
       instalado e inicializado); esta réplica usa las mismas tablas/consultas y funciona sin a3ERP.
   - **Flujo rápido (menos clics que el original):** "Buscar instancias A3ERP…", "Cargar servidor de
     a3ERP…" o "Elegir empresa a3ERP…" **encadenan** todo: eligen servidor → piden las credenciales SQL
     **solo si faltan** (la primera vez; luego quedan guardadas cifradas) → muestran las empresas → y al
     elegir una, **prueban la conexión y la guardan automáticamente**. De un botón, conectado.
3. **Validación previa** — antes de escribir nada se comprueba que: cada hoja corresponde a una
   **tabla existente**, cada columna del Excel **existe** en la tabla, y **cada valor encaja con el
   tipo** de la columna (numérico, fecha, longitud máxima de texto, rango, nullabilidad, GUID,
   bit…). Los errores se reportan con **hoja, fila, columna y motivo**.
4. **Upsert con clave autodetectada** — para decidir `INSERT` vs `UPDATE` se elige la clave en este
   orden: (a) **clave manual** elegida por el usuario para esa hoja; (b) la **clave primaria** si sus
   columnas están en el Excel; (c) el **índice único** más pequeño cuyas columnas estén todas en el
   Excel (esto resuelve tablas con PK interna/surrogate como `CUENTAS`, que en la práctica se
   identifica por `[PLACON, CUENTA]`). Si nada encaja, la hoja se **bloquea y se pide elegir la clave**
   con el botón **"Definir clave de la hoja…"**. Soporta claves compuestas e `IDENTITY_INSERT` cuando
   el Excel trae el valor de identidad.
5. **Previsualización y confirmación** — resumen por hoja/tabla (nº a insertar, a actualizar, con
   error), vista previa de las filas con la operación prevista, y **confirmación explícita** antes
   de ejecutar.
6. **Transacción con rollback** — toda la importación se ejecuta en **una única transacción**. Si
   algo falla a mitad, se hace **rollback completo**: no quedan datos a medias.
7. **Log de importación** — cada ejecución se registra (fecha/hora, archivo, tablas, nº de
   inserts/updates, resultado). Consultable desde la pestaña **Historial**.

---

## 3. Requisitos

**Para usar el artefacto final (máquina destino):**
- **Windows 10/11 o Windows Server x64.** Nada más: el `.exe` es autónomo.
- Acceso de red al servidor SQL Server y credenciales con permisos de `SELECT`/`INSERT`/`UPDATE`
  sobre las tablas destino (y lectura de metadatos).

**Para compilar/empaquetar (máquina de desarrollo):**
- **.NET 8 SDK** (LTS). Descarga: https://dotnet.microsoft.com/download/dotnet/8.0

---

## 4. Compilar y ejecutar en desarrollo

```powershell
# Restaurar y compilar
dotnet build InoutPortable.sln -c Debug

# Ejecutar la app de escritorio
dotnet run --project src\InoutPortable.App

# Ejecutar las pruebas
dotnet test
```

---

## 5. Empaquetado portable ("doble click")

```powershell
powershell -ExecutionPolicy Bypass -File build\publish.ps1
```

Genera `dist\InoutPortable\` con:
- **`InoutPortable.exe`** — el ejecutable único, self-contained + single-file (~70 MB).
- `ejemplos\` — el Excel de ejemplo y el script SQL de tablas de demo.

**Para distribuir:** comprime la carpeta `dist\InoutPortable` en un `.zip` y entrégala. El usuario
descomprime y hace **doble click en `InoutPortable.exe`**. La app crea junto al ejecutable una
carpeta **`data\`** con su configuración (`connection.json`) y el historial (`import-history.jsonl`).

> Si el `.exe` se coloca en una carpeta de solo lectura (p. ej. `Archivos de programa`), la
> configuración y el historial se guardan en `%LOCALAPPDATA%\InoutPortable`.

Detalles técnicos del empaquetado (en `build/publish.ps1`):
`--self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
-p:IncludeAllContentForSelfExtract=true` para que el driver nativo de SQL Server (SNI) viaje
dentro del propio ejecutable.

---

## 6. Cómo probarlo de principio a fin (demo)

1. En una base de datos **de pruebas** de SQL Server, ejecuta `samples\crear-tablas-demo.sql`
   (crea `dbo.Clientes` y `dbo.Articulos`).
2. Abre `InoutPortable.exe` → pestaña **Conexión**, rellena los datos, **Probar conexión** y
   **Guardar**.
3. Pestaña **Importar** → **Examinar** y elige `samples\ejemplo-import.xlsx` → **Analizar**.
   Verás el resumen: todas las filas como **Insertar**.
4. **Confirmar e importar**. Vuelve a **Analizar** el mismo archivo: ahora todo aparece como
   **Actualizar** (upsert por PK). Cambia un valor en el Excel y reimporta para verlo reflejado.

---

## 7. Convenciones del Excel (compatibles con las plantillas de a3ERP)

- **Nombre de hoja = nombre de tabla a3ERP** (p. ej. `ARTICULO`, `CLIENTES`, `CUENTAS`, `AMORTIZA`,
  `APUNTES`). Se admite `Esquema.Tabla`; si el nombre existe en varios esquemas, hay que cualificarlo.
- **Nombres amigables** — también puedes nombrar la hoja con el nombre natural (con acentos y en
  plural) y se asocia sola a la tabla real de a3ERP (`A3ErpTableAliases`): `Clientes`→`CLIENTES`,
  `Artículos`/`Productos`→`ARTICULO`, `Cuentas`→`CUENTAS`, `Almacenes`→`ALMACEN`,
  `Familias`→`FAMILIAS`, `Bancos`→`BANCOS`, `Amortizaciones`→`AMORTIZA`, `Asientos`/`Diario`→`APUNTES`.
- **Vistas y maestros repartidos** — en a3ERP moderno algunos maestros (p. ej. **`CLIENTES`**) son
  **vistas no actualizables** (el cliente se reparte entre `__CLIENTES`, `__ORGANIZACION`, …). El
  importador **detecta** esos objetos y **bloquea la hoja con un aviso claro**, porque crearlos
  requiere la lógica interna de a3ERP (usa su importación nativa para esa entidad). Los maestros que
  sí son tabla única con clave (ARTICULO, CUENTAS, ALMACEN, FAMILIAS, BANCOS, AMORTIZA…) se importan
  con normalidad. Nota: si la clave real es un **id interno** distinto del código del Excel (p. ej.
  `CUENTAS` usa `IDCUENTA`), habrá que elegir la columna clave manualmente (mejora prevista).
- **Cabecera = nombres de columna/campo** exactos (no distingue mayúsculas/minúsculas). Si la
  plantilla trae una fila de etiquetas encima de los códigos de campo, se detecta automáticamente.
- Formatos admitidos: **`.xls` (a3ERP) y `.xlsx`**, detectados por contenido.
- Las columnas **calculadas** o `rowversion/timestamp` se ignoran automáticamente.
- Las fechas pueden ir como fecha de Excel o como texto (`dd/mm/aaaa`); los números como número o
  como texto (incluido el formato español `1.234,56` y el signo final `-`).
- Los **códigos numéricos** (cuentas, códigos de artículo) se normalizan sin el `.0` sobrante. Si un
  código tiene ceros a la izquierda o más de 15 dígitos, formatéalo como **texto** en Excel para
  conservarlo intacto.

---

## 8. Decisiones de diseño y suposiciones

- **Stack:** .NET 8 (LTS) + WPF (escritorio nativo) + `Microsoft.Data.SqlClient` + `ClosedXML`.
- **Sin ODBC:** se eligió el driver TDS gestionado para lograr un artefacto verdaderamente portable.
- **Almacenamiento propio de la app:** ficheros junto al `.exe` (JSON/JSONL). No se usa SQLite ni
  otro servidor: la app no necesita infraestructura adicional. La contraseña se cifra con **DPAPI**
  (ligada al usuario de Windows; si se copia el `data\` a otro usuario/equipo habrá que reintroducirla).
- **Transacción global:** por seguridad ("no dejar datos a medias") todas las hojas se importan en
  **una sola transacción**. Implica que, si hay claves foráneas entre tablas, el orden de las hojas
  puede importar; ante un fallo, se revierte todo y se informa del motivo.
- **Detección insert/update:** se normalizan las claves para que coincidan Excel y SQL Server pese a
  diferencias de representación (p. ej. `1.50` vs `1.5`, espacios finales en `char`, mayúsculas en
  claves de texto — comparación *case-insensitive*, acorde a la colación habitual de SQL Server).
- **Filas con error:** no bloquean toda la tabla; se **omiten** y se importan las válidas (salvo que
  el problema sea estructural: columna inexistente, falta la PK, etc., que sí bloquean la hoja).

## 9. Publicar una nueva versión

```powershell
# 1) Reconstruir el ejecutable portable
powershell -ExecutionPolicy Bypass -File build\publish.ps1

# 2) Empaquetar exe + ejemplos en un zip (sin la carpeta data/)
#    (ver dist\_release en el repo; o comprime dist\InoutPortable excluyendo data\)

# 3) Crear el Release en GitHub con los binarios
gh release create v1.1.0 `
  "dist\inout-portable-v1.1.0-win-x64.zip" `
  "dist\InoutPortable\InoutPortable.exe" `
  --title "inout Portable v1.1.0" --notes "Novedades…"
```

## 10. Limitaciones conocidas / posibles mejoras

- **Maestros repartidos de a3ERP** (clientes/proveedores como vistas no actualizables): no se pueden
  crear con un upsert directo; requieren la importación nativa de a3ERP. La app lo detecta y avisa.
- **Rendimiento:** el upsert va fila a fila (parametrizado y preparado). Es robusto y suficiente
  para importaciones típicas; para volúmenes muy grandes podría añadirse `SqlBulkCopy` a tabla
  temporal + `MERGE`.
- **Tipos binarios** (`varbinary`, `image`) no se importan desde Excel (se marcan como no soportados).
- Las pruebas de tipos, parser y planificador de upsert usan **mocks de metadatos**; la ruta real
  contra SQL Server se valida con la base de datos de demo del punto 6.
