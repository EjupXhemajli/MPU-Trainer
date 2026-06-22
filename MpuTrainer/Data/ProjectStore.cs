using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using MpuTrainer.Models;

namespace MpuTrainer.Data;

public interface IProjectStore
{
    Task<List<ClientProject>> GetRecentAsync(int count = 10);
    Task<ClientProject> AddAsync(ClientProject project);
    Task UpdateAsync(ClientProject project);
    Task<ClientProject?> GetAsync(int id);
    Task DeleteAsync(int id);
    Task ReplaceQuestionsAsync(int projectId, IEnumerable<TrainingQuestion> questions);
    Task UpdateQuestionAsync(TrainingQuestion question);
}

/// <summary>
/// Datenzugriff fuer Projekte. Nutzt eine DbContextFactory, damit pro Operation
/// ein kurzlebiger Kontext erzeugt wird (threadsicher, kein Captive-Dependency).
/// </summary>
public class ProjectStore : IProjectStore
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public ProjectStore(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<List<ClientProject>> GetRecentAsync(int count = 10)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Projects
            .OrderByDescending(p => p.CreatedUtc)
            .Take(count)
            .ToListAsync();
    }

    public async Task<ClientProject> AddAsync(ClientProject project)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    public async Task UpdateAsync(ClientProject project)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Projects.Update(project);
        await db.SaveChangesAsync();
    }

    public async Task<ClientProject?> GetAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Projects
            .Include(p => p.Questions)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var project = await db.Projects.FindAsync(id);
        if (project is not null)
        {
            db.Projects.Remove(project);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Ersetzt alle Fragen eines Projekts durch eine neu generierte Liste.</summary>
    public async Task ReplaceQuestionsAsync(int projectId, IEnumerable<TrainingQuestion> questions)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var existing = db.Questions.Where(q => q.ProjectId == projectId);
        db.Questions.RemoveRange(existing);

        foreach (var q in questions)
        {
            q.Id = 0;                 // als neuer Datensatz behandeln
            q.ProjectId = projectId;
            db.Questions.Add(q);
        }
        await db.SaveChangesAsync();
    }

    public async Task UpdateQuestionAsync(TrainingQuestion question)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Questions.Update(question);
        await db.SaveChangesAsync();
    }
}
