using ExamAPI.Data;
using ExamAPI.Services.RoleMaster;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text; 
using CloudinaryDotNet;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();


var cloudConfig = builder.Configuration.GetSection("Cloudinary");
var account = new Account(
    cloudConfig["CloudName"],
    cloudConfig["ApiKey"],
    cloudConfig["ApiSecret"]
);
builder.Services.AddSingleton(new Cloudinary(account));

//  connection string
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
//  connection string end ------------//


//--services and interface ------//
builder.Services.AddScoped<ExamAPI.Services.Auth.IAuthService, ExamAPI.Services.Auth.AuthService>();
builder.Services.AddScoped<ExamAPI.Services.Common.IGenericRepository, ExamAPI.Services.Common.GenericRepository>();
builder.Services.AddScoped<ExamAPI.Services.Common.IAcademicYearService, ExamAPI.Services.Common.AcademicYearService>();
builder.Services.AddScoped<ExamAPI.Services.Permissions.IPermissionService, ExamAPI.Services.Permissions.PermissionService>();
builder.Services.AddScoped<ExamAPI.Services.CollegeDetail.ICollegeDetailService, ExamAPI.Services.CollegeDetail.CollegeDetailService>();
builder.Services.AddScoped<IRoleMasterService, RoleMasterService>();
builder.Services.AddScoped<ExamAPI.Services.Subject.ISubjectService, ExamAPI.Services.Subject.SubjectService>();
builder.Services.AddScoped<ExamAPI.Services.Exam.IExamService, ExamAPI.Services.Exam.ExamService>();
builder.Services.AddScoped<ExamAPI.Services.RegularExam.IRegularExamService, ExamAPI.Services.RegularExam.RegularExamService>();
builder.Services.AddScoped<ExamAPI.Services.GenerateHallTicket.IGenerateHallTicketService, ExamAPI.Services.GenerateHallTicket.GenerateHallTicketService>();

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

app.UseHttpsRedirection();

app.UseCors("AllowReactApp");
// Authentication & Authorization(ORDER MATTERS: Authentication (Who are you?) -> Authorization (Are you allowed?))
app.UseAuthentication();
app.UseAuthorization();
// Authentication & Authorization end

app.MapControllers();

app.Run();
