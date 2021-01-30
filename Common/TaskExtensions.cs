using Polly;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector.Common
{
    public static class TaskExtensions
    {
        /// <summary>
        /// Returns the first task whose result matches the specifiedp predicate, or null if all tasks completed without matching the predicate.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="tasks">The tasks to run.</param>
        /// <param name="predicate">The predicate to run against the tasks' results.</param>
        /// <param name="timeout">How long to wait for results before giving up.</param>
        /// <param name="cancellationToken">A cancellation token signalling no more waiting is needed.</param>
        /// <returns>A task matching </returns>
        public static Task<T?> FirstResult<T>(this Task<T>[] tasks, Func<T, bool> predicate, TimeSpan timeout, CancellationToken cancellationToken)
            where T : class
        {
            if (tasks.Length == 0)
            {
                return Task.FromResult(default(T));
            }

            var resultsCollection = new BlockingCollection<object>();
            var totalTasksProcessed = 0;
            var totalTasks = tasks.Length;

            // Tell each of the tasks to add its result to our blocking collection
            foreach (var task in tasks)
            {
                task.ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        resultsCollection.Add(t.Result);
                    }
                    else if (t.Exception != null)
                    {
                        resultsCollection.Add(t.Exception);
                    }
                    else
                    {
                        resultsCollection.Add($"No results from task {task.Id}, {task.Status}");
                    }

                    var taskCompletionCount = Interlocked.Increment(ref totalTasksProcessed);
                    if (taskCompletionCount == totalTasks)
                    {
                        resultsCollection.CompleteAdding();
                    }

                }, cancellationToken);
            }

            // Spawn a task that waits on the results from the tasks.
            var resultTask = Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return null;
                        }

                        // Grab a result and see if it passes the predicate.
                        var result = resultsCollection.Take(cancellationToken) as T;
                        if (result != null && predicate(result))
                        {
                            return result;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Take was called on the completed collection - no more items.
                    return null;
                }
            }, cancellationToken);

            try
            {
                return Policy.TimeoutAsync(timeout)
                    .ExecuteAsync(ct => resultTask, cancellationToken);
            }
            catch (Polly.Timeout.TimeoutRejectedException)
            {
                return Task.FromResult<T?>(default);
            }
        }
    }
}
