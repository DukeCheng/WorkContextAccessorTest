using Microsoft.Extensions.DependencyInjection;
using System.Security.Authentication.ExtendedProtection;
using System.Security.Cryptography;

namespace AsyncLocalTest;
public class Program
{
    //private static readonly ThreadLocal<string> threadLocal = new ThreadLocal<string>();

    //private static readonly AsyncLocal<string> asyncLocal = new AsyncLocal<string>();

    static async Task Main(string[] args)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<DefaultWorkContextAccessor>();
        serviceCollection.AddScoped<TestMethod>();

        var serviceProvider = serviceCollection.BuildServiceProvider();

        Semaphore semaphore = new Semaphore(200, 200);

        List<Task> tasks = new List<Task>();
        for (int i = 0; i < 2000; i++)
        {
            semaphore.WaitOne();
            var task = Task.Run(async () =>
             {
                 using (var scope = serviceProvider.CreateScope())
                 {
                     var accessor = scope.ServiceProvider.GetRequiredService<DefaultWorkContextAccessor>();
                     accessor.SetBackgroundWorkContext(new BackgroundWorkContext(scope.ServiceProvider));

                     var testMethod = scope.ServiceProvider.GetRequiredService<TestMethod>();
                     await testMethod.Process();
                     //accessor.DisposeContext();
                     GC.Collect();
                     semaphore.Release();
                 }

                 await Task.CompletedTask;
             });
            tasks.Add(task);
        }
        Task.WaitAll(tasks.ToArray());

        for (int i = 0; i < 5; i++)
        {
            GC.Collect();
        }

        var accessor = serviceProvider.GetRequiredService<DefaultWorkContextAccessor>();
        var context = accessor.Context;

        Console.ReadLine();

        for (int i = 0; i < 5; i++)
        {
            GC.Collect();
        }
        Console.ReadLine();
    }
}

public class TestMethod
{
    private readonly DefaultWorkContextAccessor _contextAccessor;

    public TestMethod(DefaultWorkContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }
    public async Task Process()
    {
        var context = _contextAccessor.Context;
        Console.WriteLine($"ThreadID:{Thread.CurrentThread.ManagedThreadId}-{context?.Id}");
        await Task.Delay(1000);
    }
}

public class DefaultWorkContextAccessor
{
    private static AsyncLocal<WorkContextHolder> _workContextCurrent = new AsyncLocal<WorkContextHolder>();
    public IWorkContext? Context
    {
        get
        {
            return _workContextCurrent.Value?.Context;
        }
        set
        {
            SetLocalValue(value);
        }
    }

    private static void SetLocalValue(IWorkContext? value)
    {
        var holder = _workContextCurrent.Value;
        if (holder != null)
        {
            // Clear current HttpContext trapped in the AsyncLocals, as its done.
            holder.Context = null;
        }

        if (value != null)
        {
            // Use an object indirection to hold the HttpContext in the AsyncLocal,
            // so it can be cleared in all ExecutionContexts when its cleared.
            _workContextCurrent.Value = new WorkContextHolder { Context = value };
        }
    }

    /// <summary>
    /// 后台任务设置WorkContext
    /// </summary>
    /// <param name="workContext"></param>
    public void SetBackgroundWorkContext(BackgroundWorkContext workContext)
    {
        SetLocalValue(workContext);
    }

    public void DisposeContext()
    {
        var holder = _workContextCurrent.Value;
        if (holder != null)
        {
            holder.Context.Clear();
            // Clear current HttpContext trapped in the AsyncLocals, as its done.
            holder.Context = null;
        }
    }

    private class WorkContextHolder
    {
        public IWorkContext? Context;
    }
}
public interface IWorkContext
{
    string Id { get; }
    IServiceProvider ServiceProvider { get; }

    void Clear();
}


public class BackgroundWorkContext : IWorkContext
{
    public byte[] Data = new byte[1024 * 1024 * 2];
    public IServiceProvider ServiceProvider { get; private set; }

    public string Id { get; private set; }

    public BackgroundWorkContext(IServiceProvider serviceProvider)
    {
        Id = Guid.NewGuid().ToString();
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        RandomNumberGenerator.Fill(Data);
    }

    public void Clear()
    {
        Data = new byte[0];
    }
}

