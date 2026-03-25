using ExamAPI.Data;
using ExamAPI.Services.RoleMaster;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
<<<<<<< HEAD
using Microsoft.Extensions.FileProviders;
=======
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa
using Microsoft.IdentityModel.Tokens;
using OfficeOpenXml;
using System.Text;


var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

//  connection string 
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

//  connection string end ------------//


//--services and interface ------//
builder.Services.AddScoped<ExamAPI.Services.Auth.IAuthService, ExamAPI.Services.Auth.AuthService>();
builder.Services.AddScoped<ExamAPI.Services.Common.IGenericRepository, ExamAPI.Services.Common.GenericRepository>();
builder.Services.AddScoped<ExamAPI.Services.Common.IAcademicYearService, ExamAPI.Services.Common.AcademicYearService>();
builder.Services.AddScoped<IRoleMasterService, RoleMasterService>();
builder.Services.AddScoped<ExamAPI.Services.Subject.ISubjectService, ExamAPI.Services.Subject.SubjectService>();
builder.Services.AddScoped<ExamAPI.Services.StudentMaster.IStudentMasterService, ExamAPI.Services.StudentMaster.StudentMasterService>();

//--services and interface end ------//



// JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };
});
// JWT Authentication end


//CORS config
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy => policy
            .WithOrigins("http://localhost:5173", "https://localhost:5174") //  local React URL  
            .AllowAnyMethod()
            .AllowAnyHeader());

});
//CORS config


//---mainbuild
var app = builder.Build();
//---mainbuild end

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseStaticFiles();
app.UseHttpsRedirection();

app.UseCors("AllowReactApp");
// Authentication & Authorization(ORDER MATTERS: Authentication (Who are you?) -> Authorization (Are you allowed?))
app.UseAuthentication();
app.UseAuthorization();
// Authentication & Authorization end

app.MapControllers();

app.Run();


