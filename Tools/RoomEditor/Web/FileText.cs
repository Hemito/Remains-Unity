using Microsoft.AspNetCore.Components.Forms;
namespace Web;
public static class FileText
{
 // A fully populated 4M-cell/seven-layer room is ~28 MB before object records.
 public const long MaximumBytes=128L*1024*1024;
 public static async Task<string> Read(IBrowserFile file)
 {
  using var input=file.OpenReadStream(MaximumBytes);using var bytes=new MemoryStream();
  await input.CopyToAsync(bytes);
  return new System.Text.UTF8Encoding(false,true).GetString(bytes.ToArray());
 }
}
