# Prompt para Claude Code — Herramienta de importación tipo "inout" de a3ERP

## Objetivo

Construye una aplicación web en **Laravel (PHP)** que replique el concepto de la herramienta
**inout de a3ERP**: el usuario carga un archivo **Excel** donde:

- **Cada hoja** del Excel corresponde al **nombre de una tabla** de la base de datos.
- **La fila de cabecera** de cada hoja corresponde a los **nombres de campo (columnas)** de esa tabla.
- El resto de filas son los **datos a importar**.

La app lee el Excel e **importa/actualiza (upsert)** los datos en una base de datos
**Microsoft SQL Server**.

## Alcance funcional

1. **Carga de Excel**
   - Aceptar `.xlsx` (y opcionalmente `.xls`).
   - Recorrer todas las hojas del libro. El nombre de la hoja = nombre de la tabla destino.
   - La primera fila de cada hoja = nombres de columna destino.
   - Usar una librería robusta para leer Excel (p. ej. PhpSpreadsheet).

2. **Conexión a SQL Server (configurable en la app)**
   - Pantalla de ajustes donde el usuario introduce: servidor/host, puerto, nombre de BBDD,
     usuario y contraseña.
   - Probar la conexión ("Test connection") antes de guardar.
   - Guardar la configuración de forma persistente (y la contraseña cifrada).
   - Usar el driver de SQL Server para PHP/Laravel (`sqlsrv` / `pdo_sqlsrv`).

3. **Validación previa (antes de escribir nada)**
   - Comprobar que cada hoja se corresponde con una **tabla existente** en la BBDD.
   - Comprobar que **cada columna del Excel existe** en la tabla destino.
   - **Validar tipos**: que los valores del Excel encajen con el tipo de dato de cada columna
     en SQL Server (numérico, fecha, texto, longitud máxima, nullabilidad, etc.).
   - Reportar de forma clara los errores encontrados (hoja, fila, columna, motivo).

4. **Upsert con clave primaria autodetectada**
   - Para cada tabla, **consultar la clave primaria real** en SQL Server (metadatos:
     `INFORMATION_SCHEMA` / vistas `sys`).
   - Si la fila (según su PK) **ya existe** → `UPDATE`.
   - Si **no existe** → `INSERT`.
   - Si la tabla no tiene PK detectable, avisar y permitir al usuario elegir la(s) columna(s)
     clave o abortar esa hoja.

5. **Previsualización y confirmación**
   - Antes de ejecutar, mostrar un **resumen**: por cada hoja/tabla, nº de filas a insertar,
     a actualizar, y filas con error.
   - Mostrar una **vista previa** de las filas parseadas.
   - El usuario debe **confirmar explícitamente** para lanzar la importación.

6. **Transacción con rollback**
   - Ejecutar la importación dentro de una **transacción** por lote/tabla (o global,
     según sea más seguro).
   - Si algo falla a mitad → **rollback** completo, sin dejar datos a medias.

7. **Log de importación**
   - Registrar cada ejecución: fecha/hora, archivo, tablas afectadas, nº de inserts/updates,
     errores y resultado final (éxito / rollback).
   - Poder consultar el historial desde la app.

## Portabilidad / distribución ("doble click")

El objetivo final es poder **entregar un único artefacto** a cualquier persona para que lo
ejecute **haciendo doble click**, sin instalar PHP ni configurar un servidor manualmente.

- **Elige y justifica el mejor enfoque** entre:
  - **NativePHP** (empaquetar la app Laravel como ejecutable de escritorio `.exe`), o
  - un **bundle portable** con PHP embebido + un lanzador (`.bat`/`.exe`) que arranca el
    servidor local y abre el navegador automáticamente.
- El resultado debe funcionar en **Windows** (entorno habitual de a3ERP).
- Documenta claramente el proceso de **build/empaquetado** y cómo generar el artefacto final.

## Requisitos técnicos y de calidad

- **Stack**: Laravel (última versión LTS estable), PHP moderno.
- **Base de datos destino**: Microsoft SQL Server (vía `pdo_sqlsrv`).
- Arquitectura limpia: separar lectura de Excel, validación, resolución de metadatos (PK/tipos)
  y ejecución del upsert en servicios/clases bien definidas.
- Manejo de errores claro y mensajes orientados a usuario no técnico.
- **Tests**: incluir pruebas para el parser de Excel, la validación de tipos y la lógica de
  upsert (puedes usar una BBDD SQL Server de prueba o mocks de metadatos).
- Interfaz web sencilla y clara: subir archivo → validar → previsualizar → confirmar → resultado.

## Entregables

1. El código completo de la aplicación Laravel.
2. Instrucciones de instalación y de **empaquetado portable** (README).
3. El artefacto ejecutable "doble click" (o el script/config para generarlo).
4. Tests y un archivo Excel de ejemplo para probar el flujo completo.

## Cómo quiero que trabajes

- Empieza proponiendo la **estructura del proyecto** y el enfoque de empaquetado antes de
  escribir mucho código, para validarlo.
- Ve construyendo por fases: (1) lectura Excel + parseo, (2) conexión configurable,
  (3) validación y metadatos, (4) upsert transaccional, (5) UI/preview, (6) logs,
  (7) empaquetado portable.
- Señala cualquier suposición que hagas y cualquier decisión de diseño relevante.
