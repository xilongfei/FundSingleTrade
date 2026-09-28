using System.IO;
using Microsoft.EntityFrameworkCore;

namespace FundSingleTrade.Shell.Data;

/// <summary>基金本地 SQLite 数据库上下文。</summary>
public sealed class FundDbContext : DbContext
{
    /// <summary>返回数据库与用户设置共用的本地数据目录。</summary>
    public static string DataDirectory
    {
        get
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FundSingleTrade");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>基金表。</summary>
    public DbSet<Fund> Funds => Set<Fund>();
    /// <summary>基金分类表。</summary>
    public DbSet<FundCategory> Categories => Set<FundCategory>();
    /// <summary>单笔交易表。</summary>
    public DbSet<Trade> Trades => Set<Trade>();
    /// <summary>历史净值表。</summary>
    public DbSet<FundQuote> Quotes => Set<FundQuote>();

    /// <summary>将 SQLite 文件放在用户本地应用数据目录。</summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={Path.Combine(DataDirectory, "funds.db")}");
    }

    /// <summary>定义唯一索引及分类、交易和净值的关联删除行为。</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Fund>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<FundQuote>().HasIndex(x => new { x.FundId, x.QuoteDate }).IsUnique();
        modelBuilder.Entity<FundCategory>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Fund>().Property(x => x.Code).HasMaxLength(12);
        modelBuilder.Entity<Fund>().Property(x => x.Name).HasMaxLength(100);
        modelBuilder.Entity<FundCategory>().Property(x => x.Name).HasMaxLength(50);
        modelBuilder.Entity<Fund>().HasOne(x => x.Category).WithMany(x => x.Funds)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Trade>().HasOne(x => x.Fund).WithMany(x => x.Trades)
            .HasForeignKey(x => x.FundId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FundQuote>().HasOne(x => x.Fund).WithMany(x => x.Quotes)
            .HasForeignKey(x => x.FundId).OnDelete(DeleteBehavior.Cascade);
    }
}
