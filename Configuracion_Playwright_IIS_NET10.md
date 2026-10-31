# Configuración de Playwright para ASP.NET Core .NET 10 en IIS

## 1. Problema identificado

La aplicación ASP.NET Core .NET 10 funciona correctamente en IIS, pero al ejecutar un endpoint que utiliza **Microsoft.Playwright** aparece el siguiente error:

```text
Microsoft.Playwright.PlaywrightException:
Executable doesn't exist at

C:\WINDOWS\system32\config\systemprofile\AppData\Local\ms-playwright\
chromium-1194\chrome-win\chrome.exe
```

Playwright además muestra:

```text
Looks like Playwright was just installed or updated.
Please run the following command to download new browsers
```

### Diagnóstico

El navegador Chromium sí está instalado en el equipo, pero está instalado bajo el perfil del usuario de desarrollo:

```text
C:\Users\Eder\AppData\Local\ms-playwright\
```

Mientras que la aplicación ejecutada por IIS utiliza otra identidad/perfil y Playwright intenta buscar los navegadores en:

```text
C:\Windows\System32\config\systemprofile\AppData\Local\ms-playwright\
```

Por tanto, el problema **no es que Playwright esté mal instalado en el proyecto**, sino que el navegador no está disponible en una ubicación accesible para el proceso que ejecuta IIS.

---

# 2. Confirmación del diagnóstico

En el equipo se verificó:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\ms-playwright" -Directory
```

y se obtuvo, entre otros:

```text
chromium-1194
chromium-1228
chromium_headless_shell-1194
chromium_headless_shell-1228
firefox-1495
firefox-1532
webkit-2215
webkit-2311
```

También se comprobó:

```powershell
Test-Path "$env:LOCALAPPDATA\ms-playwright\chromium-1194\chrome-win\chrome.exe"
```

Resultado:

```text
True
```

Esto confirma que Chromium 1194 existe en:

```text
C:\Users\Eder\AppData\Local\ms-playwright\chromium-1194\chrome-win\chrome.exe
```

pero no en el perfil utilizado por IIS.

---

# 3. Solución recomendada

Para una aplicación ASP.NET Core ejecutándose en IIS, se recomienda utilizar una ubicación común para los browsers de Playwright, en lugar de depender del perfil de un usuario concreto.

Se propone utilizar:

```text
C:\ProgramData\Playwright
```

y configurar la variable de entorno:

```text
PLAYWRIGHT_BROWSERS_PATH=C:\ProgramData\Playwright
```

El objetivo es que tanto la instalación de los browsers como la ejecución de la aplicación utilicen la misma ubicación.

---

# 4. ¿Qué es PLAYWRIGHT_BROWSERS_PATH?

Playwright utiliza esta variable de entorno para determinar dónde almacenar y buscar los browsers administrados por Playwright.

La documentación oficial de Playwright para .NET contempla el uso de:

```text
PLAYWRIGHT_BROWSERS_PATH
```

Documentación oficial:

[Playwright .NET — Browsers](https://playwright.dev/dotnet/docs/browsers)

La variable permite evitar que Playwright dependa de una ubicación específica del perfil del usuario.

---

# 5. Arquitectura actual

Actualmente tenemos conceptualmente:

```text
Usuario Eder
│
└── C:\Users\Eder\AppData\Local\ms-playwright
    │
    └── chromium-1194
        │
        └── chrome-win
            │
            └── chrome.exe
```

Pero IIS ejecuta la aplicación utilizando:

```text
IIS
│
└── Application Pool
    │
    └── ApplicationPoolIdentity
        │
        └── C:\Windows\System32\config\systemprofile
```

Por eso Playwright termina buscando:

```text
C:\Windows\System32\config\systemprofile\AppData\Local\ms-playwright
```

---

# 6. Arquitectura propuesta

La solución será:

```text
                         IIS
                          │
                          ▼
                  Application Pool
                  ApplicationPoolIdentity
                          │
                          ▼
                     SunatXmlApi
                          │
                          ▼
                 Microsoft.Playwright
                          │
                          │
              PLAYWRIGHT_BROWSERS_PATH
                          │
                          ▼
              C:\ProgramData\Playwright
                          │
                          ▼
                    chromium-1194
                          │
                          ▼
                       chrome.exe
```

De esta forma, la aplicación ya no depende de:

```text
C:\Users\Eder\...
```

ni de:

```text
C:\Windows\System32\config\systemprofile\...
```

---

# 7. Paso 1 — Crear la carpeta común

Abrir **PowerShell como administrador** y ejecutar:

```powershell
New-Item -ItemType Directory -Force "C:\ProgramData\Playwright"
```

La carpeta quedará:

```text
C:\ProgramData\Playwright
```

---

# 8. Paso 2 — Configurar PLAYWRIGHT_BROWSERS_PATH temporalmente

Antes de instalar el browser, configurar la variable en la sesión actual de PowerShell:

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH="C:\ProgramData\Playwright"
```

Verificar:

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH
```

Debe mostrar:

```text
C:\ProgramData\Playwright
```

Esta configuración es temporal y afecta solamente a la sesión actual de PowerShell.

Por eso posteriormente se configurará como variable de entorno del sistema.

---

# 9. Paso 3 — Ubicar el script de Playwright

En el proyecto se encontró:

```text
D:\Proyects\OdinTech\WebScraping\src\bin\Debug\net10.0\playwright.ps1
```

y también:

```text
D:\Proyects\OdinTech\WebScraping\src\bin\Release\net10.0\playwright.ps1
```

Para una aplicación que se publica en Release, utilizar preferentemente:

```text
D:\Proyects\OdinTech\WebScraping\src\bin\Release\net10.0\playwright.ps1
```

---

# 10. Paso 4 — Instalar Chromium en la nueva ubicación

Ir al directorio Release:

```powershell
cd D:\Proyects\OdinTech\WebScraping\src\bin\Release\net10.0
```

Asegurarse de que la variable esté configurada:

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH="C:\ProgramData\Playwright"
```

Luego ejecutar:

```powershell
.\playwright.ps1 install chromium
```

Playwright descargará el browser en la ubicación indicada por:

```text
PLAYWRIGHT_BROWSERS_PATH
```

El resultado esperado será una estructura similar a:

```text
C:\ProgramData\Playwright\
│
├── chromium-1194\
│   └── chrome-win\
│       └── chrome.exe
│
└── chromium_headless_shell-1194\
```

La revisión exacta puede variar dependiendo de la versión instalada de Microsoft.Playwright.

---

# 11. Paso 5 — Verificar la instalación

Ejecutar:

```powershell
Get-ChildItem "C:\ProgramData\Playwright" -Directory
```

Debería aparecer el Chromium correspondiente.

Luego verificar específicamente el ejecutable:

```powershell
Test-Path "C:\ProgramData\Playwright\chromium-1194\chrome-win\chrome.exe"
```

Si la versión utilizada por la aplicación es Chromium 1194, el resultado esperado es:

```text
True
```

## Importante

No se debe asumir que siempre será `chromium-1194`.

La versión depende de la versión de **Microsoft.Playwright** utilizada por la aplicación.

Por eso conviene revisar:

```powershell
dotnet list D:\Proyects\OdinTech\WebScraping\src\SunatXmlApi.csproj package
```

y verificar la versión de:

```text
Microsoft.Playwright
```

---

# 12. Paso 6 — Configurar la variable de entorno a nivel del sistema

La variable configurada con:

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH="C:\ProgramData\Playwright"
```

solamente existe en la sesión actual de PowerShell.

IIS no utilizará automáticamente esa variable.

Por eso se debe configurar como **System Environment Variable**.

## Opción A — Desde Windows

Abrir:

```text
System Properties
    → Advanced
        → Environment Variables
```

En:

```text
System variables
```

crear:

```text
Variable name:
PLAYWRIGHT_BROWSERS_PATH

Variable value:
C:\ProgramData\Playwright
```

---

# 13. Opción B — Configurarla mediante PowerShell

Abrir PowerShell como administrador y ejecutar:

```powershell
[System.Environment]::SetEnvironmentVariable(
    "PLAYWRIGHT_BROWSERS_PATH",
    "C:\ProgramData\Playwright",
    "Machine"
)
```

Luego verificar:

```powershell
[System.Environment]::GetEnvironmentVariable(
    "PLAYWRIGHT_BROWSERS_PATH",
    "Machine"
)
```

Debe devolver:

```text
C:\ProgramData\Playwright
```

---

# 14. Paso 7 — Dar permisos al Application Pool

La aplicación está ejecutándose con:

```text
ApplicationPoolIdentity
```

Por lo tanto, el Application Pool debe tener permisos de lectura y ejecución sobre:

```text
C:\ProgramData\Playwright
```

Supongamos que el Application Pool se llama:

```text
SunatXmlApi
```

Ejecutar como administrador:

```powershell
icacls "C:\ProgramData\Playwright" /grant "IIS APPPOOL\SunatXmlApi:(OI)(CI)(RX)" /T
```

Reemplazar:

```text
SunatXmlApi
```

por el nombre real del Application Pool.

## Permisos recomendados

Para ejecutar Chromium se recomienda otorgar:

```text
Read
Read & Execute
```

No es necesario otorgar:

```text
Full Control
```

si la aplicación únicamente necesita ejecutar los browsers ya instalados.

---

# 15. Paso 8 — Reiniciar IIS

Después de modificar una variable de entorno del sistema, reiniciar IIS para asegurarse de que los procesos nuevos reciban la variable:

```powershell
iisreset
```

También se puede reciclar específicamente el Application Pool, pero para una variable de entorno del sistema es más seguro reiniciar IIS durante esta configuración inicial.

---

# 16. Paso 9 — Verificar que IIS reciba la variable

Una vez reiniciado IIS, la aplicación debería recibir:

```text
PLAYWRIGHT_BROWSERS_PATH=C:\ProgramData\Playwright
```

Playwright utilizará esa ubicación para localizar los browsers.

El flujo será:

```text
SunatXmlApi
    │
    ▼
Microsoft.Playwright
    │
    ▼
PLAYWRIGHT_BROWSERS_PATH
    │
    ▼
C:\ProgramData\Playwright
    │
    ▼
chromium-1194
    │
    ▼
chrome.exe
```

---

# 17. Paso 10 — Probar nuevamente el endpoint

Ejecutar nuevamente el endpoint que realiza la consulta a SUNAT.

El error anterior:

```text
Executable doesn't exist at
C:\WINDOWS\system32\config\systemprofile\AppData\Local\ms-playwright\...
```

debería desaparecer.

Si la configuración es correcta, Playwright deberá localizar Chromium en:

```text
C:\ProgramData\Playwright\...
```

---

# 18. No modificar el código para poner la ruta de Chrome

No se recomienda hacer algo como:

```csharp
ExecutablePath = @"C:\Users\Eder\AppData\Local\ms-playwright\chromium-1194\chrome-win\chrome.exe";
```

Tampoco se recomienda acoplar el código a:

```csharp
ExecutablePath = @"C:\ProgramData\Playwright\chromium-1194\chrome-win\chrome.exe";
```

La razón es que Playwright administra las versiones de sus browsers.

Por ejemplo, una versión de Playwright puede utilizar:

```text
chromium-1194
```

y una versión posterior puede utilizar:

```text
chromium-1228
```

Si el código contiene directamente:

```text
chromium-1194
```

una actualización de Playwright podría romper la aplicación.

Es preferible dejar que Playwright determine automáticamente qué browser debe utilizar.

---

# 19. Tampoco copiar manualmente los browsers

No se recomienda simplemente copiar:

```text
C:\Users\Eder\AppData\Local\ms-playwright
```

hacia:

```text
C:\Windows\System32\config\systemprofile\AppData\Local\ms-playwright
```

Aunque podría solucionar el problema inmediato, deja la aplicación acoplada al perfil de ejecución de IIS.

Es más limpio utilizar:

```text
C:\ProgramData\Playwright
```

como ubicación común.

---

# 20. ¿Por qué no utilizar C:\Users\Eder?

Porque el usuario que ejecuta la aplicación en IIS no necesariamente será:

```text
Eder
```

En desarrollo puede funcionar:

```text
C:\Users\Eder\AppData\Local\ms-playwright
```

pero en otro servidor podría ser:

```text
C:\Users\OtroUsuario\...
```

o podría utilizar:

```text
ApplicationPoolIdentity
```

Por eso una ruta común como:

```text
C:\ProgramData\Playwright
```

es más apropiada para una aplicación de servidor.

---

# 21. ¿Por qué no utilizar systemprofile?

Actualmente Playwright está buscando:

```text
C:\Windows\System32\config\systemprofile\AppData\Local\ms-playwright
```

Esto ocurre porque el proceso está ejecutándose bajo una identidad de IIS que no corresponde al usuario `Eder`.

No conviene diseñar la solución alrededor de:

```text
systemprofile
```

porque la ubicación de los browsers estaría ligada al perfil interno utilizado por el proceso.

Una ubicación explícita y compartida es más fácil de administrar.

---

# 22. Consideración importante sobre versiones de Chromium

En el equipo actualmente existen:

```text
chromium-1194
chromium-1228
```

Esto puede ocurrir cuando se han utilizado diferentes versiones de Playwright.

No es necesario copiar todas las versiones a la nueva ubicación.

La instalación debe hacerse utilizando el `playwright.ps1` asociado al proyecto que se va a ejecutar.

Por ejemplo:

```powershell
cd D:\Proyects\OdinTech\WebScraping\src\bin\Release\net10.0
$env:PLAYWRIGHT_BROWSERS_PATH="C:\ProgramData\Playwright"
.\playwright.ps1 install chromium
```

Así Playwright instala la revisión correspondiente a la versión del paquete utilizada por ese proyecto.

---

# 23. Resumen de comandos

## Crear carpeta

```powershell
New-Item -ItemType Directory -Force "C:\ProgramData\Playwright"
```

## Configurar variable temporal

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH="C:\ProgramData\Playwright"
```

## Ir al directorio de Playwright

```powershell
cd D:\Proyects\OdinTech\WebScraping\src\bin\Release\net10.0
```

## Instalar Chromium

```powershell
.\playwright.ps1 install chromium
```

## Verificar browsers

```powershell
Get-ChildItem "C:\ProgramData\Playwright" -Directory
```

## Verificar ejecutable

Si corresponde a Chromium 1194:

```powershell
Test-Path "C:\ProgramData\Playwright\chromium-1194\chrome-win\chrome.exe"
```

## Configurar variable a nivel de máquina

```powershell
[System.Environment]::SetEnvironmentVariable(
    "PLAYWRIGHT_BROWSERS_PATH",
    "C:\ProgramData\Playwright",
    "Machine"
)
```

## Verificar variable

```powershell
[System.Environment]::GetEnvironmentVariable(
    "PLAYWRIGHT_BROWSERS_PATH",
    "Machine"
)
```

## Dar permisos

Ejemplo:

```powershell
icacls "C:\ProgramData\Playwright" /grant "IIS APPPOOL\SunatXmlApi:(OI)(CI)(RX)" /T
```

## Reiniciar IIS

```powershell
iisreset
```

---

# 24. Resultado esperado

Al finalizar, la aplicación debería funcionar con esta estructura:

```text
C:\
└── ProgramData
    └── Playwright
        ├── chromium-1194
        │   └── chrome-win
        │       └── chrome.exe
        │
        └── chromium_headless_shell-1194
```

Y la configuración del servidor:

```text
PLAYWRIGHT_BROWSERS_PATH
        │
        ▼
C:\ProgramData\Playwright
```

Mientras que IIS:

```text
IIS
 │
 └── ApplicationPoolIdentity
       │
       └── SunatXmlApi
             │
             └── Microsoft.Playwright
                    │
                    └── C:\ProgramData\Playwright
```

De esta manera, la aplicación no depende del usuario con el que se realizó el desarrollo o la instalación de Playwright.

---

# 25. Referencia oficial

Documentación oficial de Playwright para .NET:

**Playwright .NET — Browsers**

https://playwright.dev/dotnet/docs/browsers

Consultar específicamente la sección relacionada con **Managing browser binaries** y el uso de:

```text
PLAYWRIGHT_BROWSERS_PATH
```
