using System.Linq;
using System.Threading.Tasks;
using API.Entities;
using API.Interfaces;
using API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers
{
    public class AdminController : BaseApiController
    {
        private readonly UserManager<AppUser> userManager;
        private readonly IUnitOfWork unitofWork;
        private readonly IPhotoService photoService;
        public AdminController(UserManager<AppUser> userManager, IUnitOfWork unitofWork, IPhotoService photoService)
        {
            this.photoService = photoService;
            this.unitofWork = unitofWork;
            this.userManager = userManager;
        }

        [Authorize(Policy = "RequireAdminRole")]
        [HttpGet("users-with-roles")]
        public async Task<ActionResult> GetUsersWithRoles()
        {
            var users = await this.userManager.Users
                .Include(r => r.UserRoles)
                .ThenInclude(r => r.Role)
                .OrderBy(u => u.UserName)
                .Select(u => new
                {
                    u.Id,
                    Username = u.UserName,
                    Roles = u.UserRoles.Select(r => r.Role.Name).ToList()
                })
                .ToListAsync();

            return Ok(users);
        }

        [Authorize(Policy = "RequireAdminRole")]
        [HttpPost("edit-roles/{username}")]
        public async Task<ActionResult> EditRoles(string username, [FromQuery] string roles)
        {
            var selectedRoles = roles.Split(",").ToArray();

            var user = await this.userManager.FindByNameAsync(username);

            if (user == null) return NotFound("Could not find user");

            var userRoles = await this.userManager.GetRolesAsync(user);

            var result = await this.userManager.AddToRolesAsync(user, selectedRoles.Except(userRoles));

            if (!result.Succeeded) return BadRequest("Failed to add to roles");

            result = await this.userManager.RemoveFromRolesAsync(user, userRoles.Except(selectedRoles));

            if (!result.Succeeded) return BadRequest("Failed to remove from roles");

            return Ok(await this.userManager.GetRolesAsync(user));
        }

        [Authorize(Policy = "ModeratePhotoRole")]
        [HttpGet("photos-to-moderate")]
        public async Task<ActionResult> GetPhotosForModeration()
        {
            var photos = await this.unitofWork.PhotoRepository.GetUnapprovedPhotos();

            return Ok(photos);
        }

        [HttpPost("approve-photo/{photoId}")]
        public async Task<ActionResult> ApprovePhoto(int photoId)
        {
            var photo = await this.unitofWork.PhotoRepository.GetPhotoById(photoId);

            if (photo == null) return NotFound("Could not find photo");

            photo.IsApproved = true;

            var user = await  this.unitofWork.UserRepository.GetUserByPhotoId(photoId);

            if (!user.Photos.Any(x => x.IsMain)) photo.IsMain = true;

            await this.unitofWork.Complete();

            return Ok();
        }

        [HttpPost("reject-photo/{photoId}")]
        public async Task<ActionResult> RejectPhoto(int photoId)
        {
            var photo = await this.unitofWork.PhotoRepository.GetPhotoById(photoId);

            if (photo.PublicId != null)
            {
                var result = await this.photoService.DeletePhotoAsync(photo.PublicId);

                if (result.Result == "ok")
                {
                    this.unitofWork.PhotoRepository.RemovePhoto(photo);
                }
            }
            else
            {
                this.unitofWork.PhotoRepository.RemovePhoto(photo);
            }

            await this.unitofWork.Complete();

            return Ok();
        }
    }
}