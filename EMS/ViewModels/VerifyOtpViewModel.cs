using System.ComponentModel.DataAnnotations;

namespace EMS.ViewModels
{
    public class VerifyOtpViewModel
    {
        [Required]
        public string Email { get; set; }

        [Required]
        public string Otp { get; set; }
    }

}
