using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace CachemanCommon
{
    public enum CommandType
    {
        GET = 0,
        SET = 1,
        DELETE = 2,
        VALUE = 3,
        SERVER_ERROR
    }

    public class Command
    {

    
       

        public CommandType Action;

        public string Key;

        public long TTL;

        public long Size;

        private Command() { }


        //
        // Parses incoming commands. Commands are of the format
        // <COMMAND> <KEY> <SIZE> <TTL>r\n
        // <COMMAND> is one of 4  values (case-sensitive) GET, SET, UPDATE, DELETE
        // <KEY> is the key of the value this command acts upon
        // <SIZE> specifies size of following data in bytes . Only valid for SET and UPDATE and doesn't include trailing \r\n 
        // <TTL> specifies how long the data is valid in seconds and only exists for SET and UPDATE
        //
        public static Command  ParseCommand(string cmd) {
           
                
                CommandType cmdType ;
                long ttl = -1;
                long size = 0;
                string key = null;
               

                string[] components = cmd.Split(' ');

                System.Diagnostics.Debug.Assert(components.Length >= 2);
                

                cmdType = (CommandType) Enum.Parse(typeof(CommandType), components[0]);

                if (cmdType != CommandType.SERVER_ERROR) {

                    key = String.Intern(components[1]);


                    if (cmdType == CommandType.SET || cmdType == CommandType.VALUE) {
                        size = Convert.ToInt64(components[2], CultureInfo.InvariantCulture);

                        if (cmdType == CommandType.SET && components.Length >= 4) {
                            ttl = Convert.ToInt64(components[3], CultureInfo.InvariantCulture);
                        }
                    }
                }

                
                
                return new Command { Action = cmdType, Key = key, Size = size, TTL = ttl, };
        }

        //
        // Constructs a string representation of a command without the \r\n
        //
        public static string GetStringCommand(CommandType commandType, string key, long size,  long ttl) {

            string result = commandType.ToString() + " " + key;

            if (commandType == CommandType.SET || commandType == CommandType.VALUE) {

                result += " " + size.ToString() ;
            }

            if (commandType == CommandType.SET) {

                result += " " + ttl.ToString();
            }

            return result;
        }

    }
}
