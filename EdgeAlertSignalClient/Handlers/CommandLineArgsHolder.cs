using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Handlers
{
    public class CommandLineArgsHolder
    {
        public string[] Args { get; set; }

        public CommandLineArgsHolder(string[] args)
        {
            Args = args;
        }
    }
}
