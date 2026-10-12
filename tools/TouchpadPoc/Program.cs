using System.ComponentModel;

namespace TouchpadPoc;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine("TouchpadPoc does not accept command-line options.");
            return 2;
        }

        SessionLog? log = null;
        var exitCode = 1;
        try
        {
            log = SessionLog.Create(out var logPath);
            Console.WriteLine($"Log: {logPath}");
            exitCode = new TouchCaptureWindow(log).Run();
        }
        catch (Exception exception)
        {
            var win32Error = exception is Win32Exception win32Exception
                ? win32Exception.NativeErrorCode
                : exception.InnerException is Win32Exception innerWin32Exception
                    ? innerWin32Exception.NativeErrorCode
                    : (int?)null;
            try
            {
                log?.Write(new PocLogEvent
                {
                    Event = "Error",
                    Phase = "Fatal",
                    Message = exception.ToString(),
                    Win32Error = win32Error
                }, flush: true);
            }
            catch (Exception logException)
            {
                Console.Error.WriteLine($"Could not write fatal error to log: {logException.Message}");
            }

            Console.Error.WriteLine($"TouchpadPoc could not start or continue: {exception}");
        }
        finally
        {
            try
            {
                log?.Dispose();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Could not flush or close the session log: {exception}");
                exitCode = 1;
            }
        }

        return exitCode;
    }
}
