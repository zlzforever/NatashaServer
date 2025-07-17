using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

// using NetTopologySuite;
// using NetTopologySuite.Geometries;
// using NetTopologySuite.Geometries.Implementation;

var pluginFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
if (!Directory.Exists(pluginFolder))
{
    Directory.CreateDirectory(pluginFolder);
}

var assembliesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assemblies");
if (!Directory.Exists(assembliesFolder))
{
    Directory.CreateDirectory(assembliesFolder);
}

var files = Directory.GetFiles(pluginFolder, "*.dll");
foreach (var file in files)
{
    Assembly.LoadFile(file);
}

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
// NtsGeometryServices.Instance = new NtsGeometryServices(
//     CoordinateArraySequenceFactory.Instance,
//     PrecisionModel.Floating.Value,
//     4326, GeometryOverlay.Legacy, new CoordinateEqualityComparer());
NatashaManagement.Preheating<NatashaDomainCreator>();

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.AddConsole().AddSimpleConsole(b =>
{
    b.SingleLine = true;
    b.TimestampFormat = "yyyy-MM-dd hh:mm:ss ";
});

var lockers = new ConcurrentDictionary<string, SemaphoreSlim>();
// var jsonSerializerOptions = new JsonSerializerOptions
// {
//     PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
//     PropertyNameCaseInsensitive = false
// };

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

app.MapPost("/api/v1.0/compile", async ctx =>
{
    // 创建一个内存流用于存储请求体内容
    using var memoryStream = new MemoryStream();
    // 将请求体内容复制到内存流中
    await ctx.Request.Body.CopyToAsync(memoryStream);
    // 从内存流中获取字节数组
    var bytes = memoryStream.ToArray();
    var hash = Convert.ToHexString(MD5.HashData(bytes));
    var body = Encoding.UTF8.GetString(bytes);
    var semaphoreSlim = lockers.GetOrAdd(hash, _ => new SemaphoreSlim(1, 1));
    await semaphoreSlim.WaitAsync();

    try
    {
        var dll = Path.Combine(assembliesFolder, $"{hash}.dll");

#if !DEBUG
        if (File.Exists(dll))
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers.ContentDisposition = $"attachment; filename={hash}.dll; filename*={hash}.dll";
            await ctx.Response.Body.WriteAsync(File.ReadAllBytes(dll));
            return;
        }
#endif

        // var fields = JsonSerializer.Deserialize<List<Property>>(json, jsonSerializerOptions);
        // if (fields == null)
        // {
        //     ctx.Response.StatusCode = 204;
        //     return;
        // }

//         var className = "C_" + hash;
//         var body = new StringBuilder();
//         body.AppendLine($"public class {className}");
//         body.AppendLine("{");
//         foreach (var field in fields)
//         {
//             var fieldType = Type.GetType(field.Type);
//             if (fieldType == null)
//             {
//                 logger.LogWarning("无法找到类型: {Type}", field.Type);
//                 continue;
//             }
//
//             body.Append("    public ").Append(field.Type).AppendLine($$"""
//                                                                          @{{field.Name}} { get; set; }
//                                                                        """);
//         }
//
//         body.AppendLine("}");

        AssemblyCSharpBuilder assemblyCSharpBuilder = new();
        assemblyCSharpBuilder
            .UseRandomLoadContext()
            .UseSmartMode() //Enable smart mode
            .Add(body)
            .SetDllFilePath(dll)
            .GetAssembly();

        ctx.Response.StatusCode = 200;
        ctx.Response.Headers.ContentDisposition = $"attachment; filename={hash}.dll; filename*={hash}.dll";
        await ctx.Response.Body.WriteAsync(File.ReadAllBytes(dll));
    }
    catch (Exception e)
    {
        logger.LogError(e, "Compile failed: {Body}", body);
        ctx.Response.StatusCode = 500;
        await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(e.ToString()));
    }
    finally
    {
        semaphoreSlim.Release();
    }
});

await app.RunAsync();