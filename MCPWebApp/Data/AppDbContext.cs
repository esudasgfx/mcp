using MCPWebApp.Models;
using Microsoft.EntityFrameworkCore;

namespace MCPWebApp.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<ConfigSetting> ConfigSettings => Set<ConfigSetting>();

    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConfigSetting>(entity =>
        {
            entity.ToTable("config_settings");
            entity.HasIndex(setting => new { setting.Category, setting.Key }).IsUnique();
            entity.Property(setting => setting.Category).HasMaxLength(128).IsRequired();
            entity.Property(setting => setting.Key).HasMaxLength(256).IsRequired();
            entity.Property(setting => setting.Description).HasMaxLength(512);
            entity.Property(setting => setting.SecretReference).HasMaxLength(256);
            entity.Property(setting => setting.UpdatedBy).HasMaxLength(128);
            entity.Ignore(setting => setting.EffectiveValue);
        });

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.ToTable("chat_sessions");
            entity.HasIndex(session => session.UpdatedAt);
            entity.Property(session => session.Title).HasMaxLength(256).IsRequired();
            entity.Property(session => session.UserId).HasMaxLength(128);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("chat_messages");
            entity.HasIndex(message => new { message.ChatSessionId, message.CreatedAt });
            entity.Property(message => message.Role).HasMaxLength(32).IsRequired();
            entity.Property(message => message.ToolName).HasMaxLength(128);
            entity.Property(message => message.EventType).HasMaxLength(128);
            entity.HasOne(message => message.ChatSession)
                .WithMany(session => session.Messages)
                .HasForeignKey(message => message.ChatSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
