using Microsoft.EntityFrameworkCore;
using FileAnalysisService.Models;
using Microsoft.Extensions.Logging;

namespace FileAnalysisService.Data
{
    /// <summary>
    /// Контекст базы данных для хранения отчетов
    /// </summary>
    public class AppDbContext : DbContext
    {
        public DbSet<Report> Reports { get; set; }

        private readonly ILogger<AppDbContext> _logger;

        public AppDbContext(
            DbContextOptions<AppDbContext> options,
            ILogger<AppDbContext> logger) : base(options)
        {
            _logger = logger;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Настройка сущности Report
            modelBuilder.Entity<Report>(entity =>
            {
                entity.HasKey(e => e.Id);
                
                entity.Property(e => e.Id)
                    .HasDefaultValueSql("gen_random_uuid()");
                
                entity.Property(e => e.WorkId)
                    .IsRequired();
                
                entity.Property(e => e.StudentId)
                    .IsRequired();
                
                entity.Property(e => e.AssignmentId)
                    .IsRequired();
                
                entity.Property(e => e.Status)
                    .IsRequired()
                    .HasConversion<string>();
                
                entity.Property(e => e.CreatedAt)
                    .IsRequired()
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");
                
                entity.Property(e => e.Details)
                    .HasMaxLength(2000);
                
                entity.Property(e => e.WordCloudUrl)
                    .HasMaxLength(500);
                
                entity.Property(e => e.PlagiarismSources)
                    .HasColumnType("jsonb"); // Для PostgreSQL

                // Индексы для производительности
                entity.HasIndex(e => e.WorkId)
                    .IsUnique();
                
                entity.HasIndex(e => new { e.AssignmentId, e.StudentId });
                
                entity.HasIndex(e => e.CreatedAt);
                
                entity.HasIndex(e => e.Status);
                
                entity.HasIndex(e => e.IsPlagiarism);
            });

            _logger.LogInformation("Модель базы данных сконфигурирована");
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Автоматическое заполнение CompletedAt при завершении анализа
            var entries = ChangeTracker.Entries<Report>()
                .Where(e => e.State == EntityState.Modified);

            foreach (var entry in entries)
            {
                if (entry.Entity.Status == ReportStatus.Done && !entry.Entity.CompletedAt.HasValue)
                {
                    entry.Entity.CompletedAt = DateTime.UtcNow;
                    entry.Entity.AnalysisDuration = (entry.Entity.CompletedAt.Value - entry.Entity.CreatedAt).TotalSeconds;
                }
            }

            try
            {
                return await base.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Ошибка сохранения изменений в БД");
                throw;
            }
        }

        /// <summary>
        /// Проверка доступности БД
        /// </summary>
        public async Task<bool> CanConnectAsync()
        {
            try
            {
                return await Database.CanConnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Проверка подключения к БД не удалась");
                return false;
            }
        }

        /// <summary>
        /// Получение статистики по БД
        /// </summary>
        public async Task<DatabaseStats> GetStatsAsync()
        {
            try
            {
                var totalReports = await Reports.CountAsync();
                var plagiarismCount = await Reports.CountAsync(r => r.IsPlagiarism);
                var pendingCount = await Reports.CountAsync(r => r.Status == ReportStatus.Pending);
                var latestReport = await Reports
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync();

                return new DatabaseStats
                {
                    TotalReports = totalReports,
                    PlagiarismReports = plagiarismCount,
                    PendingReports = pendingCount,
                    LatestReportDate = latestReport?.CreatedAt,
                    DatabaseName = Database.GetDbConnection().Database
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения статистики БД");
                return new DatabaseStats { Error = ex.Message };
            }
        }
    }

    /// <summary>
    /// Статистика базы данных
    /// </summary>
    public class DatabaseStats
    {
        public int TotalReports { get; set; }
        public int PlagiarismReports { get; set; }
        public int PendingReports { get; set; }
        public DateTime? LatestReportDate { get; set; }
        public string? DatabaseName { get; set; }
        public string? Error { get; set; }
    }
}