namespace ExamAPI.Services.Permission
{
    public interface IPermissionService
    {
        Task<List<string>> GetModulesAsync();

    }
}
