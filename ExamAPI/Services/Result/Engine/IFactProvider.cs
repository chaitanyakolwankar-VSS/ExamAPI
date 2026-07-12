using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine
{
    public interface IFactProvider
    {
        string FactName { get; }
        Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster);
    }
}
