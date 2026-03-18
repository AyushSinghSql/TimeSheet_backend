using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TimeSheet.Models
{
    [Table("config_values", Schema = "public")]
    public class ConfigValue
    {
        [Key]
        [Column("name")]
        [StringLength(20)]
        public string Name { get; set; }

        [Column("value")]
        [StringLength(50)]
        public string? Value { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("id")]
        public int Id { get; set; }
    }
}