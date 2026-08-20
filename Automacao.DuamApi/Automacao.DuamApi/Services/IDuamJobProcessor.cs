namespace DuamApi.Services;

public interface IDuamJobProcessor
{
    Task ProcessarAsync(Guid jobId, string senhaCriptografada);
}
