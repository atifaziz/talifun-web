﻿using System.Collections.Generic;
using System.IO;

namespace Talifun.Web.Crusher
{
    public class NullMetaData : IMetaData
    {
        public void CreateMetaData(FileInfo outputPath, IEnumerable<FileInfo> filesToWatch)
        {
            
        }

        public bool IsMetaDataFresh(FileInfo outputPath, IEnumerable<FileInfo> filesToWatch)
        {
            return false;
        }
    }
}
