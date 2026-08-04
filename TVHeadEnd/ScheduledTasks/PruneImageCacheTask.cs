using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace TVHeadEnd.ScheduledTasks
{
    /// <summary>
    /// Removes locally cached TVHeadend images that are no longer used.
    /// </summary>
    /// <remarks>
    /// A scheduled task rather than a button on the settings page: the dashboard lists it with a run
    /// control, so it can be triggered by hand as well, and it runs on its own for the admins who
    /// never look.
    /// </remarks>
    public class PruneImageCacheTask : IScheduledTask
    {
        /// <summary>
        /// How long an image that nothing asked for is kept.
        /// </summary>
        /// <remarks>
        /// Well above the longest guide Jellyfin fetches, so an image belonging to a programme that
        /// is still listed is never deleted while it is in use.
        /// </remarks>
        private static readonly TimeSpan MaxIdleTime = TimeSpan.FromDays(30);

        private static readonly TimeSpan RunEvery = TimeSpan.FromDays(7);

        private readonly ImageCache _imageCache;
        private readonly ILogger<PruneImageCacheTask> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PruneImageCacheTask"/> class.
        /// </summary>
        /// <param name="imageCache">The image cache to prune.</param>
        /// <param name="logger">The logger.</param>
        public PruneImageCacheTask(ImageCache imageCache, ILogger<PruneImageCacheTask> logger)
        {
            _imageCache = imageCache;
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => "Clean up the TVHeadend image cache";

        /// <inheritdoc />
        public string Key => "TVHeadendPruneImageCache";

        /// <inheritdoc />
        public string Description => "Deletes cached TVHeadend channel and programme images that have not been used for 30 days. They are fetched again when they are needed.";

        /// <inheritdoc />
        public string Category => "Live TV";

        /// <inheritdoc />
        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(progress);

            progress.Report(0);

            var (files, bytes) = _imageCache.Prune(MaxIdleTime, cancellationToken);

            progress.Report(100);

            _logger.LogInformation(
                "[TVHclient] PruneImageCacheTask: removed {Files} cached image(s), freeing {Kilobytes} kB",
                files,
                bytes / 1024);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = RunEvery.Ticks
                }
            };
        }
    }
}
