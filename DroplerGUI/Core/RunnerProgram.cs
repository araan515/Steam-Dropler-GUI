using System;
using System.Threading;
using DroplerGUI.Services;

namespace DroplerGUI.Core
{
    public class RunnerProgram
    {
        private readonly TaskWorker _worker;

        public RunnerProgram()
        {
            var taskPath = "task_1";
            var statisticsService = new StatisticsService(1);
            _worker = new TaskWorker(taskPath, statisticsService, 1);
        }

        public void Run(CancellationToken token)
        {
            try
            {
                _worker.Start();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
                throw;
            }
        }

        public static void Main(string[] args)
        {
            var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                var program = new RunnerProgram();
                program.Run(cts.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
