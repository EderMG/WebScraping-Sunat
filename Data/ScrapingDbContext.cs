using Microsoft.EntityFrameworkCore;
using SunatXmlApi.Models;

namespace SunatXmlApi.Data;

public sealed class ScrapingDbContext(DbContextOptions<ScrapingDbContext> options) : DbContext(options)
{
    public DbSet<UsuarioEntity> Usuarios => Set<UsuarioEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UsuarioEntity>(entity =>
        {
            entity.HasNoKey();
            entity.ToTable("Usuario", "dbo");
            entity.Property(usuario => usuario.Usuario).HasColumnName("Usuario");
            entity.Property(usuario => usuario.Password).HasColumnName("Password");
        });
    }
}
