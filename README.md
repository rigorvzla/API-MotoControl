# API de Moto Control · despliegue en Render

Esta carpeta es un paquete **autónomo** de la API: contiene todo lo necesario
para subirla a GitHub y desplegarla en Render con Docker.

> La API es **ASP.NET Core (.NET 9)**, no Python. Render la ejecuta desde el
> `Dockerfile`; no hace falta ningún archivo `.py`.

```
Render/
├─ Dockerfile             ← imagen de la API
├─ render.yaml            ← blueprint de Render
├─ .dockerignore
├─ .gitignore
├─ README.md              ← este archivo
├─ MotoControl.Api/       ← API (endpoints, servicios, acceso a datos)
└─ MotoControl.Shared/    ← DTOs compartidos con el frontend
```

---

## 1. Subir esta carpeta a GitHub

La **raíz del repositorio** debe ser esta carpeta `Render` (la que contiene el
`Dockerfile`).

1. GitHub → **New repository** → nombre por ejemplo `motocontrol-api` →
   **público o privado** (Render funciona con ambos) → sin README ni licencia.
2. Desde PowerShell:

```powershell
cd "D:\Documentos\Visual Studio 2022\Projects\MotoControl\Render"

git init
git add .
git commit -m "API MotoControl para Render"
git branch -M main
git remote add origin https://github.com/TU-USUARIO/motocontrol-api.git
git push -u origin main
```

Comprueba que en la raíz del repositorio aparezcan `Dockerfile` y
`render.yaml`. Si no están ahí, Render no los encontrará.

---

## 2. Crear el servicio en Render

### Opción recomendada: Blueprint

1. Render → **New → Blueprint**.
2. Conecta el repositorio `motocontrol-api`.
3. Rama `main`.
4. Render lee `render.yaml` y muestra el servicio **`motocontrolweb`**
   (Web Service, runtime Docker, plan Free).
5. Pulsa **Apply** y completa las variables que pida:
   - `ConnectionStrings__Cockroach`
   - `Seed__AdminUser`
   - `Seed__AdminPassword`
6. Espera el build (5–10 minutos la primera vez).

### Opción manual: Web Service

1. Render → **New → Web Service** → conecta el repositorio.
2. Nombre: `motocontrolweb` (para conservar la URL anterior).
3. Runtime / Language: **Docker**.
4. Branch: `main`.
5. Root Directory: vacío.
6. Dockerfile Path: `./Dockerfile`.
7. Docker Build Context: `.`
8. Health Check Path: `/api/health`.
9. Plan: **Free**.
10. Agrega las variables de la tabla siguiente y pulsa **Create Web Service**.

---

## 3. Variables de entorno

Todas se pueden editar después en **Settings → Environment**.

| Variable | Valor |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__Cockroach` | `Host=TU-HOST.cockroachlabs.cloud;Port=26257;Username=TU_USUARIO;Database=MotoControlSystem;SSL Mode=VerifyFull;Password=TU_PASSWORD` |
| `Jwt__Key` | Se genera sola con el blueprint (o una clave larga y secreta) |
| `Cors__AllowedOrigins__0` | `https://rigorvzla.github.io` |
| `Seed__AdminUser` | El usuario administrador que quieras |
| `Seed__AdminPassword` | Tu contraseña de administrador |
| `App__TimeZone` | `America/Caracas` |

Notas:

- **Nunca** subas la contraseña de CockroachDB al repositorio. Solo vive en
  las variables de Render.
- `Cors__AllowedOrigins__0` es el **origen** del frontend: sin barra final y
  sin la ruta `/MotoControl`.
- Si CockroachDB Cloud tiene *IP allowlist*, permite el tráfico de Render o
  desactiva la restricción para ese usuario.

---

## 4. Comprobar que quedó arriba

```
https://motocontrolweb.onrender.com/api/health
```

Respuesta esperada:

```json
{ "estado": "ok", "baseDatos": "CockroachDB CCL ..." }
```

El plan gratuito **duerme** el servicio tras un rato sin uso: la primera
petición puede tardar 30–60 segundos.

---

## 5. Administrador inicial (controlado desde Render)

El administrador se crea **en el primer arranque**, leyendo
`Seed__AdminUser` y `Seed__AdminPassword`, y **solo si la tabla `usuarios` no
tiene ningún usuario con rol `admin`**.

La base `MotoControlSystem` quedó sin administradores, así que el primer
arranque de este servicio creará el tuyo con los valores que pongas en Render.

Para cambiarlo más adelante:

1. Edita `Seed__AdminUser` / `Seed__AdminPassword` en **Environment**.
2. Borra el admin actual (CockroachDB → SQL Shell):

   ```sql
   DELETE FROM usuarios WHERE rol = 'admin';
   ```

3. Reinicia el servicio (**Manual Deploy → Restart**). Al arrancar lo crea de
   nuevo con las credenciales nuevas.

> Si el admin ya existe, el seed no se ejecuta y la contraseña de Render no
> cambia nada: por eso hay que borrar la fila antes.

Después de entrar, puedes cambiar la contraseña desde **Mi cuenta**; ese
cambio sí queda guardado en la base y no lo pisa Render.

---

## 6. Conectar el frontend

El frontend lee la URL de la API desde
`RigorVzla_App_Web/wwwroot/appsettings.json`:

```json
{
  "ApiBaseUrl": "https://motocontrolweb.onrender.com/"
}
```

Si el servicio se llama `motocontrolweb`, la URL no cambia y no hay que tocar
nada. Si Render te asigna otra URL, actualiza ese archivo y vuelve a publicar
GitHub Pages.

---

## 7. Problemas frecuentes

| Síntoma | Solución |
|---|---|
| El build falla en Render | Revisa que `Dockerfile` y `MotoControl.Api/` estén en la raíz del repo. |
| `/api/health` responde 404 con `x-render-routing: no-server` | No hay deploy activo: revisa **Events / Logs** y vuelve a desplegar. |
| Error de conexión a la base | Cadena incorrecta o falta `SSL Mode=VerifyFull`. |
| Error de CORS en el navegador | `Cors__AllowedOrigins__0` debe ser exactamente `https://rigorvzla.github.io`. |
| El login dice usuario o contraseña incorrectos | El seed no se ejecutó porque ya existía un admin; bórralo y reinicia (sección 5). |
| El PDF falla | La imagen ya instala `libfontconfig1`, `libfreetype6` y fuentes DejaVu. |
