using System;
using System.Collections.Generic;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace TuEmpresa.StorageUtils
{
    /// <summary>
    /// Clase genérica para generar URLs con SAS (Shared Access Signature)
    /// para contenedores y blobs en Azure Blob Storage. 
    /// Se puede reutilizar en cualquier proyecto .NET que necesite exponer blobs de forma segura.
    /// </summary>
    public class SasUrlGenerator
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly StorageSharedKeyCredential _sharedKeyCredential;

        /// <summary>
        /// Constructor que recibe accountName y accountKey. Con esto construye:
        ///  - Un StorageSharedKeyCredential (para firmar SAS)
        ///  - Un BlobServiceClient (para operaciones sobre la cuenta)
        /// </summary>
        /// <param name="accountName">Nombre de la cuenta de Storage (por ejemplo: "miaccount")</param>
        /// <param name="accountKey">Clave secreta de la cuenta (pero no la expongas en texto plano en tu código).</param>
        public SasUrlGenerator(string accountName, string accountKey)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("El accountName no puede estar vacío.", nameof(accountName));
            if (string.IsNullOrWhiteSpace(accountKey))
                throw new ArgumentException("El accountKey no puede estar vacío.", nameof(accountKey));

            _sharedKeyCredential = new StorageSharedKeyCredential(accountName, accountKey);

            // Cadena de conexión construida “al vuelo” usando las credenciales proporcionadas;
            // puedes sustituirlo por un ConnectionString si lo prefieres.
            var connectionString =
                $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";

            _blobServiceClient = new BlobServiceClient(connectionString);
        }

        /// <summary>
        /// Genera un SAS de contenedor (Container SAS) con los permisos especificados y lo devuelve
        /// como un string que contiene únicamente la porción del SAS (query string), sin la URL base.
        /// Luego tú puedes concatenarla a cualquier URL de blob que esté dentro de ese contenedor.
        /// </summary>
        /// <param name="containerName">Nombre del contenedor (por ejemplo: "luegopago-uploads")</param>
        /// <param name="permisos">Permisos sobre el contenedor (Read, List, Write, Delete, etc.)</param>
        /// <param name="validezEnMinutos">Cuánto tiempo (en minutos) será válido este SAS (por ejemplo, 15)</param>
        /// <returns>
        /// Un string que representa el query string completo del SAS, empezando en "sv=..." hasta "sig=...".
        /// Ej: "sv=2020-08-04&st=2025-06-04T10%3A00%3A00Z&se=2025-06-04T10%3A15%3A00Z&sr=c&sp=rl&sig=XXXX..."
        /// </returns>
        public string GenerarContainerSasQueryString(
            string containerName,
            BlobContainerSasPermissions permisos,
            int validezEnMinutos = 15)
        {
            if (string.IsNullOrWhiteSpace(containerName))
                throw new ArgumentException("El nombre del contenedor no puede estar vacío.", nameof(containerName));
            if (validezEnMinutos <= 0)
                throw new ArgumentException("La validez en minutos debe ser mayor que cero.", nameof(validezEnMinutos));

            // Construir el BlobSasBuilder para contenedor:
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerName,
                Resource = "c", // "c" indica recurso de tipo contenedor
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-1),
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(validezEnMinutos)
            };

            // Asignar permisos solicitados (Read = r, List = l, Write = w, etc.)
            sasBuilder.SetPermissions(permisos);

            // Generar la query string (sin la URL base).  
            // ToSasQueryParameters() firma con StorageSharedKeyCredential.
            var sasQuery = sasBuilder
                .ToSasQueryParameters(_sharedKeyCredential)
                .ToString();

            return sasQuery;
        }

        /// <summary>
        /// Genera un SAS de blob individual (Blob SAS) para un blob específico dentro de un contenedor.
        /// Devuelve la URL completa (URI) que incluye el query string SAS.
        /// </summary>
        /// <param name="containerName">Nombre del contenedor donde reside el blob.</param>
        /// <param name="blobPath">
        /// Ruta relativa al blob dentro del contenedor.  
        /// Ejemplo: "seller/3683/camara de comercio ultrafit.pdf" (la SDK se encarga de codificar espacios).
        /// </param>
        /// <param name="permisos">Permisos sobre el blob (Read, Write, Delete, etc.). En la mayoría de casos basta con Read (r).</param>
        /// <param name="validezEnMinutos">Tiempo en minutos que será válido este SAS para el blob.</param>
        /// <returns>
        /// Una Uri que representa la URL completa con SAS para acceder a ese blob.  
        /// Ej: "https://miaccount.blob.core.windows.net/mi-contenedor/ruta/al/blob.pdf?sv=...&sig=..."
        /// </returns>
        public Uri GenerarBlobSasUrl(
            string containerName,
            string blobPath,
            BlobSasPermissions permisos,
            int validezEnMinutos = 15)
        {
            if (string.IsNullOrWhiteSpace(containerName))
                throw new ArgumentException("El nombre del contenedor no puede estar vacío.", nameof(containerName));
            if (string.IsNullOrWhiteSpace(blobPath))
                throw new ArgumentException("La ruta del blob no puede estar vacía.", nameof(blobPath));
            if (validezEnMinutos <= 0)
                throw new ArgumentException("La validez en minutos debe ser mayor que cero.", nameof(validezEnMinutos));

            // 1. Obtener el BlobContainerClient y luego el BlobClient para la ruta dada
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            // Opcional: verificar existencia del contenedor
            // if (!containerClient.Exists())
            //     throw new InvalidOperationException($"El contenedor '{containerName}' no existe.");

            var blobClient = containerClient.GetBlobClient(blobPath);

            // Opcional: verificar existencia del blob
            // if (!blobClient.Exists())
            //     throw new InvalidOperationException($"El blob '{blobPath}' no existe en el contenedor '{containerName}'.");

            // 2. Construir el BlobSasBuilder para este blob
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerName,
                BlobName = blobPath,
                Resource = "b", // "b" indica recurso de tipo blob
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-1),
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(validezEnMinutos)
            };

            // 3. Especificar los permisos que queramos (p. ej., solo lectura)
            sasBuilder.SetPermissions(permisos);

            // 4. Generar el token SAS (Query string)
            var sasToken = sasBuilder
                .ToSasQueryParameters(_sharedKeyCredential)
                .ToString();

            // 5. Construir la URL completa: URI base del blob + "?" + token SAS
            var uriBuilder = new UriBuilder(blobClient.Uri)
            {
                Query = sasToken
            };

            return uriBuilder.Uri;
        }

        /// <summary>
        /// Genera, para un conjunto de rutas de blobs, su correspondiente URL SAS (método síncrono).
        /// Ideal para cuando quieres devolver al cliente un diccionario ruta → URL completa con SAS.
        /// </summary>
        /// <param name="containerName">Nombre del contenedor común a todos los blobs.</param>
        /// <param name="blobPaths">Lista de rutas (strings) de blobs dentro de ese contenedor.</param>
        /// <param name="permisos">Permisos de SAS para todos los blobs (por ejemplo: Read).</param>
        /// <param name="validezEnMinutos">Tiempo de validez en minutos para cada SAS individual.</param>
        /// <returns>
        /// Diccionario donde la clave es la ruta original (p. ej. "seller/3683/archivo.pdf") 
        /// y el valor es la URL con SAS (Uri absoluto).
        /// </returns>
        public Dictionary<string, Uri> GenerarSasUrlParaVariosBlobs(
            string containerName,
            IEnumerable<string> blobPaths,
            BlobSasPermissions permisos,
            int validezEnMinutos = 15)
        {
            if (string.IsNullOrWhiteSpace(containerName))
                throw new ArgumentException("El nombre del contenedor no puede estar vacío.", nameof(containerName));
            if (blobPaths == null)
                throw new ArgumentNullException(nameof(blobPaths));
            if (validezEnMinutos <= 0)
                throw new ArgumentException("La validez en minutos debe ser mayor que cero.", nameof(validezEnMinutos));

            var resultado = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);

            foreach (var ruta in blobPaths)
            {
                if (string.IsNullOrWhiteSpace(ruta))
                    continue;

                // Para cada ruta, creamos un BlobClient y luego generamos su SAS Url
                var blobUrl = GenerarBlobSasUrl(containerName, ruta, permisos, validezEnMinutos);
                resultado[ruta] = blobUrl;
            }

            return resultado;
        }

        /// <summary>
        /// Alternativa asíncrona que verifica existencia de cada blob antes de generar el SAS.
        /// </summary>
        public async System.Threading.Tasks.Task<Dictionary<string, Uri>> GenerarSasUrlParaVariosBlobsAsync(
            string containerName,
            IEnumerable<string> blobPaths,
            BlobSasPermissions permisos,
            int validezEnMinutos = 15)
        {
            if (string.IsNullOrWhiteSpace(containerName))
                throw new ArgumentException("El nombre del contenedor no puede estar vacío.", nameof(containerName));
            if (blobPaths == null)
                throw new ArgumentNullException(nameof(blobPaths));
            if (validezEnMinutos <= 0)
                throw new ArgumentException("La validez en minutos debe ser mayor que cero.", nameof(validezEnMinutos));

            var resultado = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            // Opcional: verificar existencia del contenedor
            if (!await containerClient.ExistsAsync())
                throw new InvalidOperationException($"El contenedor '{containerName}' no existe.");

            foreach (var ruta in blobPaths)
            {
                if (string.IsNullOrWhiteSpace(ruta))
                    continue;

                var blobClient = containerClient.GetBlobClient(ruta);

                // Verificar que el blob exista; si no existe, lo omitimos
                if (!await blobClient.ExistsAsync())
                    continue;

                var sasUri = GenerarBlobSasUrl(containerName, ruta, permisos, validezEnMinutos);
                resultado[ruta] = sasUri;
            }

            return resultado;
        }
    }
}
