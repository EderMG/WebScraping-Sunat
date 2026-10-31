# SunatXmlApi

API ASP.NET Core para emitir un JWT y consultar/descargar XML de comprobantes usando el flujo SUNAT Clave SOL.

## Configuración

No se guardan credenciales en el repositorio. Configure estas variables de entorno antes de iniciar:

| Variable | Uso |
| --- | --- |
| `Jwt__SigningKey` | Clave HMAC secreta de al menos 32 bytes. |
| `ConnectionStrings__Scraping` | Cadena de conexión a SQL Server, base `Scraping`, que contiene `dbo.Usuario`. |
| `Sunat__LoginUrl` | URL vigente de inicio de sesión Clave SOL de SUNAT. |

Configure la cadena de conexión fuera de los archivos versionados, por ejemplo con User Secrets desde la carpeta del proyecto:

```powershell
dotnet user-secrets set "ConnectionStrings:Scraping" "Server=LAPTOP-EDER;Database=Scraping;User Id=sa;Password=<contraseña>;TrustServerCertificate=True;"
```

Reemplace `<contraseña>` por la clave de SQL Server al ejecutar el comando. La API usa Entity Framework Core para validar `Usuario` y `Password` en `dbo.Usuario`; el token solo se genera si ambos valores coinciden exactamente.

## Depuración en VS Code

`dotnet run -c Debug` compila y ejecuta la aplicación en configuración Debug, pero no conecta un depurador. Para depurar, abre esta carpeta del proyecto en VS Code, instala la extensión C# (o C# Dev Kit), coloca breakpoints y presiona `F5`. Elige `.NET Launch (SunatXmlApi)` para que VS Code compile e inicie la API bajo el depurador. Si ya la iniciaste con `dotnet run -c Debug`, elige `.NET Attach (dotnet run)` y selecciona el proceso `SunatXmlApi`.

La aplicación también necesita las variables de configuración indicadas arriba (por ejemplo, mediante User Secrets); si falta alguna, puede detenerse durante el inicio.

La automatización del inicio de sesión requiere Chromium de Playwright. Después de compilar, instale el navegador desde la carpeta del proyecto:

```powershell
.\bin\Debug\net10.0\playwright.ps1 install chromium
```

## Logs

Los logs se escriben en consola y en `Logs/sunat-AAAA-MM-DD.log`, dentro de la raíz de contenido de la aplicación. La carpeta se crea automáticamente. Los archivos y las fechas de cada entrada usan UTC; cada línea contiene un objeto JSON con nivel, categoría, mensaje y excepción completa (incluidas la pila y las excepciones internas).

Puede cambiar la carpeta con `Logging:File:Directory` en `appsettings.json` o con la variable de entorno `Logging__File__Directory`, por ejemplo `D:\Logs\SunatXmlApi`. Las rutas relativas se resuelven desde la raíz de contenido. Al iniciar, la aplicación informa la ruta absoluta en el log y verifica que pueda escribir allí.

Se registran los mensajes según `Logging:LogLevel`, incluidos los errores de SUNAT, las excepciones controladas de la API, las solicitudes con formato inválido y los fallos de inicio. Las cancelaciones del cliente se registran como advertencias. Mantenga habilitados los niveles `Error` y `Critical` para conservar errores y excepciones. Los archivos se acumulan por día, sin eliminación automática.

## Endpoints

### `POST /api/auth/token`

Request:

```json
{
  "usuario": "api-user",
  "contrasena": "api-password"
}
```

Response:

```json
{
  "accessToken": "<jwt>",
  "tokenType": "Bearer",
  "expiresAtUtc": "2026-10-17T22:00:00Z"
}
```

### `POST /api/comprobantes/xml`

Requiere `Authorization: Bearer <jwt>` y las cabeceras:

| Header | Descripción |
| --- | --- |
| `X-Ruc-Emisor` | RUC del emisor (11 dígitos). |
| `X-Usuario-Sol` | Usuario SOL. |
| `X-Clave-Sol` | Clave SOL. |

Body:

```json
{
  "rucComprobante": "20101267467",
  "tipo": "01",
  "serie": "F413",
  "numero": "320",
  "tipoServicio": "01"
}
```

Response:

```json
{
  "nombreArchivo": "20101267467-01-F413-320.xml",
  "valorArchivo": "<contenido XML codificado en Base64>"
}
```

Los errores usan el formato `{ "success": false, "error": { "code": "...", "message": "..." } }`.
Use HTTPS y un gestor de secretos para las variables de entorno en cualquier despliegue. Las credenciales SOL nunca se incluyen en mensajes de log.
