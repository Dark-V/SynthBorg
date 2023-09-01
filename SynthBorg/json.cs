using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthBorg
{
    public class Config
    {
        public string Voice { get; set; }
        public string voice_allow_msg { get; set; }
        public string voice_not_allow_msg { get; set; }
        public bool websocketinfo { get; set; }
        public bool mod { get; set; }
        public bool sub { get; set; }
        public int hotkey { get; set; }
        public bool vip { get; set; }
        public string channel { get; set; }
        public string token { get; set; }
        public int Speed { get; set; }
        public List<string> IgnoredUsers { get; set; }
        public List<string> WhitelistedUsers { get; set; }
    }
}
