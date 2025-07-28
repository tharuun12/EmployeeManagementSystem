using System.ComponentModel.DataAnnotations;

namespace EMS.ViewModels
{
    public class RegisterViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }

        public string PhoneNumber { get; set; }

        public string FullName { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Passwords do not match")]
        public string ConfirmPassword { get; set; }

        //[Required]
        //public string Role { get; set; }
        public int EmployeeId { get; set; }       // For Edit
        public int DepartmentId { get; set; }     // For dropdown selection
        public int? ManagerId { get; set; }       // Nullable, since some employees might not have a manager
        public int LeaveBalance { get; set; }
    }
}
