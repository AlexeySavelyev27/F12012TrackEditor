#pragma once
#include "PSSGNode.h"
#include <string>

class PSSGWriter {
public:
    PSSGWriter(const PSSGNode& root);
    void Save(const std::string& path);
private:
    void WriteNode(std::ostream& stream, const PSSGNode& node);
    PSSGNode root_;
};
