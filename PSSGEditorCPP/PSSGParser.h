#pragma once
#include "PSSGNode.h"
#include <string>

class PSSGParser {
public:
    PSSGParser(const std::string& path);
    PSSGNode Parse();
private:
    PSSGNode ReadNode(std::istream& stream);
    std::string path_;
};
