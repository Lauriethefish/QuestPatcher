using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher
{
    public class VersionUtil
    {
        private static int[] splitVersionString(string str)
        {
            int[] result = new int[3]; // QuestPatcher only supports semvar
            string[] sections = str.Split('.');
            
            if(sections.Length != 3)
            {
                throw new Exception("The wrong number of version numbers (" + sections.Length + ") was found in the mod. QuestPatcher only supports semvar");
            }

            for(int i = 0; i < 3; i++)
            {
                result[i] = int.Parse(sections[i]);
            }

            return result;
        }

        // Returns true if version B is at least at version A
        public static bool isVersionAtLeast(string versionAStr, string versionBStr)
        {
            int[] versionA = splitVersionString(versionAStr);
            int[] versionB = splitVersionString(versionBStr);

            if (versionA.Length != versionB.Length)
            {
                throw new Exception("Cannot compare version numbers with different lengths");
            }

            for(int i = 0; i < versionA.Length; i++)
            {
                if(versionB[i] < versionA[i])
                {
                    return false;
                }

                if (versionB[i] > versionA[i])
                {
                    return true;
                }
            }

            return true;
        }

        // Returns true if version B is less than or equal to version A 
        public static bool isVersionAtMost(string versionAStr, string versionBStr)
        {
            int[] versionA = splitVersionString(versionAStr);
            int[] versionB = splitVersionString(versionBStr);

            if (versionA.Length != versionB.Length)
            {
                throw new Exception("Cannot compare version numbers with different lengths");
            }

            for (int i = 0; i < versionA.Length; i++)
            {
                if (versionB[i] > versionA[i])
                {
                    return false;
                }

                if(versionB[i] < versionA[i])
                {
                    return true;
                }
            }

            return true;
        }

        // Checks that version A and B are the same, however version A can contain wildcards like 0.8.*
        public static bool equalsWithWildcards(string versionAStr, string versionBStr)
        {
            string[] versionA = versionAStr.Split('.');
            string[] versionB = versionBStr.Split('.');

            if (versionA.Length != 3 || versionB.Length != 3)
            {
                throw new Exception("Wrong length of version number");
            }

            for(int i = 0; i < versionA.Length; i++)
            {
                if(versionA[i] != versionB[i] && versionA[i] != "*")
                {
                    return false;
                }
            }

            return true;
        }


        // Uses a simple version range format to compare versions
        // If a range starts with "^", any versions at or above that will return true
        // If a range contains 2 versions separated by a dash (-), then it'll check that version is between those versions (inclusive)
        // Otherwise, it'll compare them with wildcards. So 0.5.* would match any version like 0.5.<something>
        public static bool isVersionWithinRange(string version, string range)
        {
            if(range.StartsWith('^'))
            {
                return isVersionAtLeast(range.Substring(1), version);
            }

            if(range.Contains('-'))
            {
                string[] versions = range.Split("-");

                return isVersionAtLeast(versions[0], version) && isVersionAtMost(versions[1], version);
            }

            return equalsWithWildcards(range, version);
        }
    }
}
